using Client.Core.Sessions.Hybrid;
using Client.GStreamer.Interop;
using Client.GStreamer.WebRTC.Sdp;
using Gst.Sdp;
using Gst.WebRTC;
using Gst;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// Runs the offer/answer rounds between the description built from the sections and webrtcbin.
/// </summary>
/// <remarks>
/// The SFU speaks ORTC style parameters and never sees SDP: the description only exists between
/// this class and webrtcbin. The flow is the same one mediasoup-client uses, with the SFU as the
/// offerer:
/// <list type="number">
/// <item>the transport parameters are turned into a remote offer;</item>
/// <item>webrtcbin answers it, which yields our own DTLS fingerprint;</item>
/// <item>the fingerprint goes back to the SFU through transport.connect, without which the
/// handshake cannot complete.</item>
/// </list>
/// </remarks>
internal sealed partial class Negotiator
{
    private readonly TransportCore _core;
    private readonly Func<List<SdpMediaSection>> _buildSections;
    private readonly Action _attachPending;
    private readonly Action _observeTransports;
    private readonly Action<IReadOnlyList<SdpMediaSection>> _roundCompleted;
    private readonly ILogger _logger;

    private TransportParameters? _transportParameters;
    private bool _dtlsParametersPublished;
    private bool _negotiationInProgress;
    private bool _negotiationPending;

    /// <param name="attachPending">
    /// Links the capture branches of new sending sections, between applying the remote description
    /// and creating the answer.
    /// </param>
    /// <param name="observeTransports">Called after every round, once the transports exist.</param>
    /// <param name="roundCompleted">
    /// Called with the sections of a round once webrtcbin applied both of its descriptions.
    /// </param>
    public Negotiator(TransportCore core, Func<List<SdpMediaSection>> buildSections, Action attachPending,
        Action observeTransports, Action<IReadOnlyList<SdpMediaSection>> roundCompleted, ILogger logger)
    {
        _core = core;
        _buildSections = buildSections;
        _attachPending = attachPending;
        _observeTransports = observeTransports;
        _roundCompleted = roundCompleted;
        _logger = logger;
    }

    /// <summary>
    /// Raised once the local description is set. The parameters have to be delivered to the SFU
    /// with transport.connect before the DTLS handshake can succeed.
    /// </summary>
    public event Action<DtlsParameters>? LocalDtlsParametersReady;

    /// <summary>
    /// Answer produced by webrtcbin, kept for diagnostics.
    /// </summary>
    public string? LocalDescription { get; private set; }

    public DtlsParameters? LocalDtlsParameters { get; private set; }

    /// <summary>
    /// The transport every round is negotiated against. Set before the first round.
    /// </summary>
    public void SetTransport(TransportParameters transportParameters) => _transportParameters = transportParameters;

    /// <summary>
    /// Runs one offer/answer round against the current set of sections. The transport itself is
    /// not touched: ICE and DTLS stay up and the streams already running are not interrupted.
    /// </summary>
    public bool Negotiate()
    {
        lock (_core.StateLock)
        {
            if (_core.IsDisposed || _core.WebRTC == null || _transportParameters == null)
                return false;

            // The promises run asynchronously, so a round that arrives while another one is in
            // flight is coalesced into a single follow-up.
            if (_negotiationInProgress)
            {
                _negotiationPending = true;
                return true;
            }

            _negotiationInProgress = true;
        }

        var sections = _buildSections();
        var remoteSdp = SdpBuilder.Build(_transportParameters, sections);

        LogRemoteDescriptionBuilt(_core.SessionId, remoteSdp);

        return SetRemoteDescription(remoteSdp, sections);
    }

    private void CompleteNegotiation()
    {
        bool pending;

        lock (_core.StateLock)
        {
            _negotiationInProgress = false;
            pending = _negotiationPending;
            _negotiationPending = false;
        }

        // Also reached from promise callbacks, which must not emit signals themselves
        if (pending)
            Task.Run(() => Negotiate());
    }

    private bool SetRemoteDescription(string remoteSdp, IReadOnlyList<SdpMediaSection> sections)
    {
        if (SDPMessage.NewFromText(remoteSdp, out var sdpMessage) != SDPResult.Ok || sdpMessage == null)
        {
            LogRemoteDescriptionParseFailed(_core.SessionId);
            CompleteNegotiation();
            return false;
        }

        // The description takes the message over
        using var description = WebRTCSessionDescription.New(WebRTCSDPType.Offer, sdpMessage);

        var applied = CreatePromise(promise =>
        {
            if (TryGetError(promise, out var error))
            {
                LogRemoteDescriptionApplyFailed(_core.SessionId, error);
                _core.Connected.TrySetResult(false);
                CompleteNegotiation();
                return;
            }

            // The promise is answered while webrtcbin holds its own lock, which requesting a pad
            // takes too, so the pads are linked from another thread.
            Task.Run(() =>
            {
                if (_core.IsDisposed)
                    return;

                try
                {
                    _attachPending();
                }
                catch (Exception exception)
                {
                    LogOutboundAttachFailed(exception, _core.SessionId);
                }

                RequestRetransmissions(sections);
                CreateAnswer(sections);
            });
        });

        _core.WebRTC!.EmitSignal("set-remote-description", description, applied);

        return true;
    }

    /// <summary>
    /// Has the jitter buffer of every video section ask the SFU for the packets it lost, with
    /// NACKs, instead of waiting for the next keyframe. The SFU resends them on the original SSRC.
    /// Applied to the transceivers the remote description just created, before the answer, which
    /// keeps the nack feedback only where it is set; the jitter buffer reads it when its stream
    /// starts. The voices are left as they are: Opus covers a loss with FEC, and the SFU keeps no
    /// packets of a voice to resend.
    /// </summary>
    private void RequestRetransmissions(IReadOnlyList<SdpMediaSection> sections)
    {
        var webrtc = _core.WebRTC;

        if (webrtc == null)
            return;

        for (var index = 0; index < sections.Count; index++)
        {
            if (sections[index].Kind != "video")
                continue;

            try
            {
                using var transceiver = GstNative.EmitGetObject<Gst.GObject.Object>(webrtc, "get-transceiver", index);

                if (transceiver != null)
                    GstNative.SetBoolean(transceiver.Handle, "do-nack", true);
            }
            catch (Exception exception)
            {
                LogRetransmissionRequestsFailed(exception, _core.SessionId, index);
            }
        }
    }

    private void CreateAnswer(IReadOnlyList<SdpMediaSection> sections)
    {
        var webrtc = _core.WebRTC;

        if (webrtc == null)
            return;

        var created = CreatePromise(promise =>
        {
            if (TryGetError(promise, out var error))
            {
                LogAnswerCreateFailed(_core.SessionId, error);
                _core.Connected.TrySetResult(false);
                CompleteNegotiation();
                return;
            }

            using var reply = promise.GetReply();
            WebRTCSessionDescription? answer = null;

            if (reply != null && reply.HasField("answer"))
            {
                using var value = reply.GetValue("answer");
                answer = value.GetBoxed<WebRTCSessionDescription>();
            }

            if (answer == null)
            {
                LogAnswerMissing(_core.SessionId);
                _core.Connected.TrySetResult(false);
                CompleteNegotiation();
                return;
            }

            // Applying the answer emits signals, which must not happen under the lock webrtcbin
            // holds while it answers this promise
            Task.Run(() =>
            {
                // The description is ours: the value it was read from held a copy
                using (answer)
                {
                    if (_core.IsDisposed)
                        return;

                    try
                    {
                        ApplyLocalDescription(answer, sections);
                    }
                    catch (Exception exception)
                    {
                        LogAnswerApplyFailed(exception, _core.SessionId);
                        CompleteNegotiation();
                    }
                }
            });
        });

        using var options = Structure.NewEmpty("options");
        webrtc.EmitSignal("create-answer", options, created);
    }

    private void ApplyLocalDescription(WebRTCSessionDescription answer, IReadOnlyList<SdpMediaSection> sections)
    {
        var applied = CreatePromise(promise =>
        {
            if (TryGetError(promise, out var error))
            {
                LogLocalDescriptionApplyFailed(_core.SessionId, error);
                return;
            }

            // Off the thread of webrtcbin, which answers the promise under its lock
            Task.Run(() => _core.Notify(() => _roundCompleted(sections)));
        });

        _core.WebRTC!.EmitSignal("set-local-description", answer, applied);

        using (var sdp = answer.GetSdp())
            LocalDescription = sdp.AsText();

        LogLocalDescription(_core.SessionId, LocalDescription);

        _core.Pipeline!.SetState(State.Playing);

        _observeTransports();

        // The certificate does not change between rounds, so the SFU is told about it once.
        if (!_dtlsParametersPublished)
        {
            _dtlsParametersPublished = true;
            PublishLocalDtlsParameters(LocalDescription);
        }

        CompleteNegotiation();
    }

    /// <summary>
    /// Extracts our certificate fingerprint and DTLS role from the answer. The SFU verifies the
    /// peer certificate against exactly this value.
    /// </summary>
    private void PublishLocalDtlsParameters(string localSdp)
    {
        string? algorithm = null;
        string? fingerprint = null;
        var setup = "active";

        foreach (var rawLine in localSdp.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("a=fingerprint:", StringComparison.OrdinalIgnoreCase) && fingerprint == null)
            {
                var value = line["a=fingerprint:".Length..].Split(' ', 2);

                if (value.Length == 2)
                {
                    algorithm = value[0].Trim();
                    fingerprint = value[1].Trim();
                }
            }
            else if (line.StartsWith("a=setup:", StringComparison.OrdinalIgnoreCase))
            {
                setup = line["a=setup:".Length..].Trim();
            }
        }

        if (algorithm == null || fingerprint == null)
        {
            LogLocalFingerprintMissing(_core.SessionId);
            return;
        }

        // setup:active means we open the DTLS connection, which is the client role for the SFU.
        var role = setup switch
        {
            "active" => "client",
            "passive" => "server",
            _ => "auto"
        };

        LocalDtlsParameters = new DtlsParameters(role, [new DtlsFingerprint(algorithm, fingerprint)]);

        LogLocalDtlsParameters(_core.SessionId, role, algorithm, fingerprint);

        LocalDtlsParametersReady?.Invoke(LocalDtlsParameters);
    }

    /// <summary>
    /// Wraps a promise handler: it runs on a GStreamer thread through a native callback, where an
    /// exception would tear the process down.
    /// </summary>
    private Promise CreatePromise(Action<Promise> handler)
    {
        // Not disposed here: webrtcbin answers the promise later, and the binding releases the
        // wrapper once it is collected, see GstBusWatcher
        return Promise.NewWithChangeFunc(promise =>
        {
            try
            {
                handler(promise);
            }
            catch (Exception exception)
            {
                LogPromiseFailed(exception, _core.SessionId);
            }
        });
    }

    private static bool TryGetError(Promise promise, out string error)
    {
        error = string.Empty;

        using var reply = promise.GetReply();

        // An empty reply is the normal outcome for the description setters.
        if (reply == null || !reply.HasField("error"))
            return false;

        error = GstNative.ReadErrorMessage(reply) ?? "unknown";

        return true;
    }
}
