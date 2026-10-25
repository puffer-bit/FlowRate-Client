using Client.Core.Settings;
using Client.Application.Ports;
using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Client.GStreamer.Audio;
using Client.GStreamer.Interop;
using Gst;
using Gst.WebRTC;
using Microsoft.Extensions.Logging;
using Shared.Enums.WebRTC;
using Shared.Models.Media;
using Task = System.Threading.Tasks.Task;
using WebRTCICEConnectionState = Gst.WebRTC.WebRTCICEConnectionState;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// Minimal WebRTC transport on top of webrtcbin, which owns ICE, DTLS-SRTP and RTCP.
/// </summary>
/// <remarks>
/// This class owns the pipeline and webrtcbin and the order they are built and taken down in. The
/// work on them is done by its parts: <see cref="Negotiator"/> runs the offer/answer rounds over
/// the <see cref="MediaSections"/>, <see cref="IncomingTracks"/> and <see cref="OutgoingTracks"/>
/// keep the tracks of each direction, <see cref="IncomingStreamPlayer"/> plays what arrives,
/// <see cref="TransportStateObserver"/> follows the connection, <see cref="SpeakingIndicator"/>
/// reports who speaks and <see cref="TransportStats"/> logs how the streams fare.
/// </remarks>
public sealed partial class WebRTCTransport : IWebRTCTransport
{
    private readonly TransportCore _core;
    private readonly MediaSections _sections = new();
    private readonly IncomingTracks _incoming;
    private readonly OutgoingTracks _outgoing;
    private readonly OutgoingRetransmission _retransmission;
    private readonly Negotiator _negotiator;
    private readonly TransportStateObserver _states;
    private readonly WebRTCBinTuning _tuning;
    private readonly SpeakingIndicator _speaking;
    private readonly TransportStats _stats;

    private readonly bool _isPlaybackEnabled;
    private readonly ILogger<WebRTCTransport> _logger;
    private GstBusWatcher? _busWatcher;

    /// <inheritdoc cref="Negotiator.LocalDtlsParametersReady"/>
    public event Action<DtlsParameters>? LocalDtlsParametersReady
    {
        add => _negotiator.LocalDtlsParametersReady += value;
        remove => _negotiator.LocalDtlsParametersReady -= value;
    }

    public event Action<WebRTCConnectionState>? ConnectionStateChanged;

    public event Action<Pad, Caps>? IncomingStreamReady;

    public event Action<int, bool>? SpeakingChanged;

    public event Action<int, MediaSourceType, RawVideoFrame>? VideoFrameReady;

    public event Action<IReadOnlyList<string>>? InboundsReady;

    public WebRTCConnectionState ConnectionState => _states.ConnectionState;

    /// <inheritdoc cref="TransportStateObserver.IceConnectionState"/>
    public WebRTCICEConnectionState IceConnectionState => _states.IceConnectionState;

    /// <inheritdoc cref="TransportStateObserver.DtlsTransportState"/>
    public WebRTCDTLSTransportState DtlsTransportState => _states.DtlsTransportState;

    /// <inheritdoc cref="Negotiator.LocalDescription"/>
    public string? LocalDescription => _negotiator.LocalDescription;

    public DtlsParameters? LocalDtlsParameters => _negotiator.LocalDtlsParameters;

    /// <param name="devices">Needed only to send a <see cref="DeviceAudioInput"/>.</param>
    /// <param name="isPlaybackEnabled">Whether incoming audio is played on the output device.</param>
    public WebRTCTransport(int sessionId, GstAudioDeviceProvider? devices, ILogger<WebRTCTransport> logger,
        bool isPlaybackEnabled = true)
    {
        _logger = logger;
        _isPlaybackEnabled = isPlaybackEnabled;

        _core = new TransportCore(sessionId, logger);
        _incoming = new IncomingTracks(_core, _sections, Negotiate,
            inboundIds => InboundsReady?.Invoke(inboundIds), logger);
        _retransmission = new OutgoingRetransmission(_core, logger);
        _outgoing = new OutgoingTracks(_core, _sections, devices, Negotiate, _retransmission,
            (userId, sourceType, frame) => VideoFrameReady?.Invoke(userId, sourceType, frame), logger);
        _states = new TransportStateObserver(_core, state => ConnectionStateChanged?.Invoke(state), logger);
        _tuning = new WebRTCBinTuning(_core, logger);
        _negotiator = new Negotiator(_core, () => _sections.Build(_incoming.Find, _outgoing.Find),
            _outgoing.AttachPending, _states.ObserveTransports, _incoming.OnRoundCompleted, logger);
        _speaking = new SpeakingIndicator(sessionId, CollectSpeaking,
            (userId, isSpeaking) => SpeakingChanged?.Invoke(userId, isSpeaking), logger);
        _stats = new TransportStats(_core, _incoming.FindBySsrc, _outgoing.FindBySsrc, _outgoing.ReadSendQueues,
            ReportMedia, logger);
    }

    private bool Negotiate() => _negotiator.Negotiate();

    // How the voices went over the last minute, on both sides of the transport
    private void ReportMedia()
    {
        _outgoing.ReportGates();
        _core.Player?.ReportVoiceArrival(_incoming.ResolveSectionUser);
    }

    /// <summary>
    /// Builds the pipeline and starts the negotiation against the given transport. The media
    /// sections come from the inbound tracks registered so far.
    /// </summary>
    public bool Connect(TransportParameters transportData)
    {
        _negotiator.SetTransport(transportData);

        lock (_core.StateLock)
        {
            if (_core.IsDisposed || _core.Pipeline != null)
                return false;

            var pipeline = Pipeline.New($"webrtc_transport_{_core.SessionId}");
            // Left to choose, the pipeline runs on the clock of the most upstream element, the
            // microphone. Every xrun of the capture device then jumps the clock the voices are
            // played by, and the sink resyncs with a click; a replaced microphone takes the clock
            // away with it. The system clock goes on evenly whatever the devices do.
            using (var clock = SystemClock.Obtain())
                pipeline.UseClock(clock);
            var webrtc = ElementFactory.Make("webrtcbin", "webrtc");

            _core.Pipeline = pipeline;
            _core.WebRTC = webrtc;

            if (webrtc == null)
            {
                LogWebrtcbinCreateFailed(_core.SessionId);
                return false;
            }

            // max-bundle keeps every section on a single ICE transport, which is what the SFU
            // expects and what keeps all of its clients on one port.
            Global.UtilSetObjectArg(webrtc, "bundle-policy", "max-bundle");
            // The jitter buffer, in ms: what a voice is delayed by to even out the network, unless the
            // media settings gave its track another. The video streams get a longer one, see
            // WebRTCBinTuning.ConfigureRtpBin
            webrtc.SetProperty("latency", 100u);

            _tuning.EnableConnectivityCheckKeepalives();
            _retransmission.Attach(webrtc);

            pipeline.Add(webrtc);

            _tuning.ConfigureRtpBin(_incoming.FindBySsrc);

            _states.Connect(webrtc);
            _core.Player = new IncomingStreamPlayer(pipeline, _logger, _core.SessionId, _incoming.ResolveSectionVolume,
                OnIncomingVideoFrame, _isPlaybackEnabled);
            webrtc.PadAdded += OnPadAdded;
            webrtc.PadRemoved += OnPadRemoved;

            // Also keeps the bus from piling up the messages nobody reads
            _busWatcher = new GstBusWatcher(pipeline.GetBus(), MessageType.Element, _outgoing.OnElementMessage,
                $"webrtc_transport_{_core.SessionId}", _logger);

            _speaking.Start();
            _stats.Start();

            pipeline.SetState(State.Ready);
        }

        return Negotiate();
    }

    public void Disconnect()
    {
        Pipeline? pipeline;
        Element? webrtc;

        lock (_core.StateLock)
        {
            pipeline = _core.Pipeline;
            webrtc = _core.WebRTC;

            _core.Pipeline = null;
            _core.WebRTC = null;
        }

        Teardown(pipeline, webrtc);
    }

    /// <summary>
    /// Completes once webrtcbin reports a connected peer connection, which means ICE and DTLS
    /// both succeeded.
    /// </summary>
    public async Task<bool> WaitUntilConnectedAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var completed = await Task.WhenAny(_core.Connected.Task, Task.Delay(timeout, cancellationToken));

        return completed == _core.Connected.Task && _core.Connected.Task.Result;
    }

    public void RegisterInbound(InboundTrack inbound, double volume) => _incoming.Register(inbound, volume);

    public void UnregisterInbound(int userId, MediaType mediaType, MediaSourceType mediaSourceType) =>
        _incoming.Unregister(userId, mediaType, mediaSourceType);

    public void UnregisterAllUserInbounds(int userId) => _incoming.UnregisterAllOfUser(userId);

    public void SetInboundVolume(int userId, MediaSourceType sourceType, double volume) =>
        _incoming.SetVolume(userId, sourceType, volume);

    public void SetInboundVoiceMuted(bool isMuted) => _incoming.SetVoiceMuted(isMuted);

    public bool StartOutbound(OutboundTrack outbound, AudioInput input, double volume, double? voiceGateThresholdDb,
        NoiseSuppressionLevel noiseSuppression) =>
        _outgoing.Start(outbound, input, volume, voiceGateThresholdDb, noiseSuppression);

    /// <inheritdoc cref="OutgoingTracks.Start(OutboundTrack, Element, double, bool, double?, NoiseSuppressionLevel)"/>
    public bool StartOutbound(OutboundTrack outbound, Element source, double volume, bool isMicrophone,
        double? voiceGateThresholdDb, NoiseSuppressionLevel noiseSuppression) =>
        _outgoing.Start(outbound, source, volume, isMicrophone, voiceGateThresholdDb, noiseSuppression);

    public bool StartOutbound(OutboundTrack outbound, VideoInput input, ScreenShareSettings settings) =>
        _outgoing.Start(outbound, input, settings);

    public void StopOutbound(string outboundId) => _outgoing.Stop(outboundId);

    public bool SetOutboundMuted(string outboundId, bool isMuted) => _outgoing.SetMuted(outboundId, isMuted);

    public void SetOutboundVolume(string outboundId, double volume) => _outgoing.SetVolume(outboundId, volume);

    public void SetOutboundInput(string outboundId, AudioInput input, NoiseSuppressionLevel noiseSuppression) =>
        _outgoing.SetInput(outboundId, input, noiseSuppression);

    public void SetVoiceGateThreshold(string outboundId, double? thresholdDb) =>
        _outgoing.SetVoiceGateThreshold(outboundId, thresholdDb);

    /// <summary>
    /// Hands a decoded frame to whoever draws it. Runs on the pump thread of the section.
    /// </summary>
    private void OnIncomingVideoFrame(uint mline, RawVideoFrame frame)
    {
        InboundTrack? inbound;

        using (_sections.Enter())
            inbound = _incoming.FindBySection(mline);

        if (inbound != null)
            VideoFrameReady?.Invoke(inbound.UserId, inbound.SourceType, frame);
    }

    /// <summary>
    /// Adds the users who speak at the moment: ours by the meters of the outgoing tracks, the
    /// others by the meters of the sections their audio arrives on.
    /// </summary>
    private void CollectSpeaking(long now, HashSet<int> speaking)
    {
        _outgoing.CollectSpeaking(now, speaking);

        var sections = _core.Player?.GetSpeakingSections(now) ?? [];

        if (sections.Count == 0)
            return;

        using (_sections.Enter())
        {
            foreach (var mline in sections)
            {
                // The sound of a shown screen plays through the same kind of section, but is not speech
                if (_incoming.FindBySection(mline) is { MediaType: MediaType.Audio, SourceType: MediaSourceType.Microphone } inbound)
                    speaking.Add(inbound.UserId);
            }
        }
    }

    private void OnPadAdded(object? sender, Element.PadAddedSignalArgs args)
    {
        var pad = args.NewPad;

        if (pad.Direction != PadDirection.Src)
            return;

        // Valid for the handlers below only: the wrapper owns a reference and gives it back here
        using var caps = pad.GetCurrentCaps() ?? pad.QueryCaps(null);

        LogIncomingStream(_core.SessionId, pad.Name ?? string.Empty, caps.ToString());

        // Runs on a streaming thread through a native callback, where an exception would tear the
        // process down.
        _core.Notify(() => _core.Player?.Attach(pad, caps));

        IncomingStreamReady?.Invoke(pad, caps);
    }

    private void OnPadRemoved(object? sender, Element.PadRemovedSignalArgs args)
    {
        var pad = args.OldPad;

        if (pad.Direction != PadDirection.Src)
            return;

        LogIncomingStreamEnded(_core.SessionId, pad.Name ?? string.Empty);

        _core.Notify(() => _core.Player?.Detach(pad));
    }

    public void Dispose()
    {
        Pipeline? pipeline;
        Element? webrtc;

        lock (_core.StateLock)
        {
            if (_core.IsDisposed)
                return;

            _core.IsDisposed = true;

            pipeline = _core.Pipeline;
            webrtc = _core.WebRTC;

            _core.Pipeline = null;
            _core.WebRTC = null;
        }

        Teardown(pipeline, webrtc);
    }

    /// <summary>
    /// Stops everything that works on the pipeline, shuts it down and lets go of every wrapper of
    /// its objects.
    /// </summary>
    /// <remarks>
    /// The wrappers matter as much as the pipeline: one that is merely dropped holds its object
    /// until it is collected and the release queue of the binding is drained, see GstBusWatcher.
    /// A forgotten wrapper of webrtcbin keeps the whole transport meanwhile: its ICE agent, its
    /// sockets and its threads.
    /// </remarks>
    private void Teardown(Pipeline? pipeline, Element? webrtc)
    {
        _core.Player?.Stop();

        _outgoing.StopCapture();

        _busWatcher?.Dispose();
        _busWatcher = null;

        _speaking.Stop();
        _stats.Stop();

        _core.Connected.TrySetResult(false);
        pipeline?.SetState(State.Null);

        // Nobody reads the bus any more, and shutting down posts onto it. A message holds the object
        // that posted it, so what is left on the bus is dropped
        if (pipeline != null)
        {
            using var bus = pipeline.GetBus();
            bus.SetFlushing(true);
        }

        // Only now: the wrappers of pads with a probe have to outlive the streaming threads
        _core.Player?.Dispose();

        _outgoing.Release(webrtc);
        _states.ReleaseWatches();
        _tuning.Release();
        _retransmission.Release();

        // The handlers go before the wrappers. webrtcbin removes the pads of its incoming streams
        // only while it is being destroyed, and a pad-removed handled then would wrap an object
        // in the middle of its destruction: a reference that stops it for good.
        if (webrtc != null)
        {
            webrtc.PadAdded -= OnPadAdded;
            webrtc.PadRemoved -= OnPadRemoved;

            _states.Disconnect(webrtc);
        }

        pipeline?.Dispose();
        webrtc?.Dispose();
    }
}
