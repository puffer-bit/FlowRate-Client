using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Client.Application.Ports;
using Client.Core.Media;
using Client.Core.Settings;
using Client.Core.Sessions.Hybrid;
using Client.WebRTC.Sdp;
using Com.Flowrate.Rtc;
using Microsoft.Extensions.Logging;
using Shared.Enums.WebRTC;
using Shared.Models.Media;

namespace Client.Droid.Platform.Media.WebRTC;

/// <summary>
/// Voice of a hybrid session over libwebrtc, see VoicePeer.java. The description of the SFU is built
/// the way the desktop builds it for webrtcbin; libwebrtc answers it, captures the microphone and
/// plays the voices. Screens are neither shown nor watched.
/// </summary>
internal sealed partial class AndroidWebRTCTransport : IWebRTCTransport
{
    // How often the levels of the voices are read for the speaking indicator
    private static readonly TimeSpan LevelsInterval = TimeSpan.FromMilliseconds(200);

    // The audio level, 0..1, above which a voice counts as speech, and how long it is held after it falls
    private const double SpeakingLevel = 0.02;
    private static readonly TimeSpan SpeakingHold = TimeSpan.FromMilliseconds(400);

    private readonly int _sessionId;
    private readonly ILogger<AndroidWebRTCTransport> _logger;
    private readonly Lock _lock = new();

    // The m-sections by m-line index; never removed, only emptied and reused, which keeps every mid stable
    private readonly List<Slot> _slots = [];
    private readonly Dictionary<string, InboundTrack> _inbounds = [];
    private readonly Dictionary<string, OutboundTrack> _outbounds = [];
    private readonly HashSet<string> _readyInbounds = [];
    private readonly Dictionary<(int UserId, MediaSourceType SourceType), double> _volumes = [];
    private readonly Dictionary<int, long> _speakingUntil = [];
    private readonly TaskCompletionSource<bool> _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private VoicePeer? _peer;
    private TransportParameters? _transportParameters;
    private Timer? _levelsTimer;
    private bool _isVoiceMuted;
    private bool _isDisposed;
    private bool _isNegotiating;
    private bool _isNegotiationPending;
    private bool _isDtlsPublished;
    private IReadOnlyList<SdpMediaSection> _roundSections = [];

    public event Action<DtlsParameters>? LocalDtlsParametersReady;
    public event Action<WebRTCConnectionState>? ConnectionStateChanged;
    public event Action<int, bool>? SpeakingChanged;
    public event Action<int, MediaSourceType, RawVideoFrame>? VideoFrameReady;
    public event Action<IReadOnlyList<string>>? InboundsReady;
    public event Action<TransportStatsSample>? StatsSampled;

    public WebRTCConnectionState ConnectionState { get; private set; } = WebRTCConnectionState.New;

    public AndroidWebRTCTransport(int sessionId, ILogger<AndroidWebRTCTransport> logger)
    {
        _sessionId = sessionId;
        _logger = logger;
    }

    public bool Connect(TransportParameters transportParameters)
    {
        lock (_lock)
        {
            if (_isDisposed || _peer != null)
                return false;

            _transportParameters = transportParameters;
            _peer = new VoicePeer(new PeerListener(this));
        }

        VoicePeer.SetCommunicationMode(global::Android.App.Application.Context, true);
        _levelsTimer = new Timer(_ => RequestLevels(), null, LevelsInterval, LevelsInterval);

        return Negotiate();
    }

    public void Disconnect()
    {
        VoicePeer? peer;

        lock (_lock)
        {
            peer = _peer;
            _peer = null;
        }

        Close(peer);
    }

    public void Dispose()
    {
        VoicePeer? peer;

        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            peer = _peer;
            _peer = null;
        }

        Close(peer);
    }

    private void Close(VoicePeer? peer)
    {
        _levelsTimer?.Dispose();
        _levelsTimer = null;
        _connected.TrySetResult(false);

        if (peer == null)
            return;

        peer.Close();
        peer.Dispose();
        VoicePeer.SetCommunicationMode(global::Android.App.Application.Context, false);
        ReportEveryoneSilent();
    }

    public async Task<bool> WaitUntilConnectedAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var completed = await Task.WhenAny(_connected.Task, Task.Delay(timeout, cancellationToken));

        return completed == _connected.Task && _connected.Task.Result;
    }

    public void RegisterInbound(InboundTrack inbound, double volume)
    {
        // Screens are not watched here
        if (inbound.MediaType != MediaType.Audio)
            return;

        lock (_lock)
        {
            _volumes[(inbound.UserId, inbound.SourceType)] = volume;

            if (!_inbounds.TryAdd(inbound.Id, inbound))
                return;

            // A rejoin makes the SFU create a new consumer for a track we may still hold
            foreach (var stale in _inbounds.Values.Where(item => item.Id != inbound.Id && item.UserId == inbound.UserId &&
                                                               item.SourceType == inbound.SourceType).ToList())
                ReleaseLocked(stale.Id);

            var slot = _slots.FirstOrDefault(item => !item.IsPlaceholder && !item.IsSend && item.InboundId == null &&
                                                     item.Carries(inbound.MimeType));

            if (slot == null)
                _slots.Add(slot = new Slot());

            slot.InboundId = inbound.Id;
        }

        LogInboundRegistered(_sessionId, inbound.UserId, inbound.SSRC);
        Negotiate();
    }

    public void UnregisterInbound(int userId, MediaType mediaType, MediaSourceType mediaSourceType) =>
        Release(inbound => inbound.UserId == userId && inbound.MediaType == mediaType &&
                           inbound.SourceType == mediaSourceType);

    public void UnregisterAllUserInbounds(int userId) => Release(inbound => inbound.UserId == userId);

    private void Release(Func<InboundTrack, bool> isReleased)
    {
        lock (_lock)
        {
            var released = _inbounds.Values.Where(isReleased).Select(inbound => inbound.Id).ToList();

            if (released.Count == 0)
                return;

            foreach (var inboundId in released)
                ReleaseLocked(inboundId);
        }

        Negotiate();
    }

    private void ReleaseLocked(string inboundId)
    {
        _inbounds.Remove(inboundId);
        _readyInbounds.Remove(inboundId);

        foreach (var slot in _slots.Where(slot => slot.InboundId == inboundId))
            slot.InboundId = null;
    }

    public void SetInboundVolume(int userId, MediaSourceType sourceType, double volume)
    {
        lock (_lock)
        {
            if (!_inbounds.Values.Any(inbound => inbound.UserId == userId && inbound.SourceType == sourceType))
                return;

            _volumes[(userId, sourceType)] = volume;
        }

        ApplyVolumes();
    }

    public void SetInboundVoiceMuted(bool isMuted)
    {
        lock (_lock)
            _isVoiceMuted = isMuted;

        ApplyVolumes();
    }

    private void ApplyVolumes()
    {
        List<(string Mid, double Volume)> volumes;
        VoicePeer? peer;

        lock (_lock)
        {
            peer = _peer;
            volumes = [];

            for (var index = 0; index < _slots.Count; index++)
            {
                if (_slots[index].InboundId is { } inboundId && _inbounds.TryGetValue(inboundId, out var inbound))
                    volumes.Add((index.ToString(CultureInfo.InvariantCulture), GetVolume(inbound)));
            }
        }

        foreach (var (mid, volume) in volumes)
            peer?.SetVolume(mid, volume);
    }

    private double GetVolume(InboundTrack inbound) =>
        _isVoiceMuted && inbound.SourceType == MediaSourceType.Microphone
            ? 0
            : _volumes.GetValueOrDefault((inbound.UserId, inbound.SourceType), 1.0);

    public bool StartOutbound(OutboundTrack outbound, AudioInput input, double volume, VoiceGateOptions? voiceGate,
        NoiseSuppressionLevel noiseSuppression, bool isEchoCancellationEnabled)
    {
        // Only the microphone: there is nothing else to capture sound from here
        if (outbound.MediaType != MediaType.Audio || outbound.SourceType != MediaSourceType.Microphone)
            return false;

        lock (_lock)
        {
            if (_peer == null || !_outbounds.TryAdd(outbound.Id, outbound))
                return false;

            var slot = _slots.FirstOrDefault(item => item.IsSend && item.OutboundId == null && item.Carries(outbound.MimeType));

            if (slot == null)
                _slots.Add(slot = new Slot { IsSend = true });

            slot.OutboundId = outbound.Id;
            _peer.SetMicrophoneEnabled(true);
        }

        LogOutboundStarted(_sessionId, outbound.SSRC);

        return Negotiate();
    }

    public bool StartOutbound(OutboundTrack outbound, VideoInput input, ScreenShareSettings settings) => false;

    public void StopOutbound(string outboundId)
    {
        lock (_lock)
        {
            if (!_outbounds.Remove(outboundId))
                return;

            foreach (var slot in _slots.Where(slot => slot.OutboundId == outboundId))
                slot.OutboundId = null;
        }

        Negotiate();
    }

    public bool SetOutboundMuted(string outboundId, bool isMuted)
    {
        lock (_lock)
        {
            if (_peer == null || !_outbounds.ContainsKey(outboundId))
                return false;

            _peer.SetMicrophoneEnabled(!isMuted);
            return true;
        }
    }

    // libwebrtc processes the microphone itself: its own gain control, noise suppression and echo cancelling
    public void SetOutboundVolume(string outboundId, double volume)
    {
    }

    public void SetOutboundInput(string outboundId, AudioInput input, NoiseSuppressionLevel noiseSuppression,
        bool isEchoCancellationEnabled)
    {
    }

    public void SetVoiceGate(string outboundId, VoiceGateOptions? voiceGate)
    {
    }

    /// <summary>
    /// Runs an offer/answer round over the current sections. A round asked for while another one runs
    /// is coalesced into a single follow-up.
    /// </summary>
    private bool Negotiate()
    {
        VoicePeer peer;
        string offer;
        string[] sendMids;
        long[] sendSsrcs;

        lock (_lock)
        {
            if (_isDisposed || _peer == null || _transportParameters == null)
                return false;

            if (_isNegotiating)
            {
                _isNegotiationPending = true;
                return true;
            }

            _isNegotiating = true;
            peer = _peer;

            var sections = BuildSections();
            var sends = sections
                .Select((section, index) => (section, Outbound: _slots[index].OutboundId))
                .Where(item => item.Outbound != null && _outbounds.ContainsKey(item.Outbound))
                .ToList();

            _roundSections = sections;
            offer = SdpBuilder.Build(_transportParameters, sections);
            sendMids = sends.Select(item => item.section.Mid).ToArray();
            sendSsrcs = sends.Select(item => (long)_outbounds[item.Outbound!].SSRC).ToArray();
        }

        LogRemoteDescription(_sessionId, offer);

        try
        {
            peer.Negotiate(offer, sendMids, sendSsrcs);
            return true;
        }
        catch (Exception exception)
        {
            LogNegotiationFailed(_sessionId, exception.Message);
            CompleteNegotiation();
            return false;
        }
    }

    /// <summary>
    /// Turns the slots into sections. An empty set still produces one, because a description without
    /// media carries no transport to bring up. Only inside the lock.
    /// </summary>
    private List<SdpMediaSection> BuildSections()
    {
        if (_slots.Count == 0)
            _slots.Add(new Slot { IsPlaceholder = true });

        return _slots.Select((slot, index) =>
        {
            var mid = index.ToString(CultureInfo.InvariantCulture);

            if (slot.IsPlaceholder)
                return new SdpMediaSection { Mid = mid, Kind = "audio", Direction = SdpDirection.SendOnly, Codec = RtpCodec.Opus() };

            if (slot.IsSend)
            {
                if (slot.OutboundId != null && _outbounds.TryGetValue(slot.OutboundId, out var outbound))
                {
                    slot.LastCodec = RtpCodec.FromOutbound(outbound);
                    return new SdpMediaSection { Mid = mid, Kind = "audio", Direction = SdpDirection.RecvOnly, Codec = slot.LastCodec };
                }
            }
            else if (slot.InboundId != null && _inbounds.TryGetValue(slot.InboundId, out var inbound))
            {
                slot.LastCodec = RtpCodec.FromInbound(inbound);
                return new SdpMediaSection
                {
                    Mid = mid,
                    Kind = "audio",
                    Direction = SdpDirection.SendOnly,
                    Codec = slot.LastCodec,
                    SSRC = inbound.SSRC,
                    InboundId = inbound.Id
                };
            }

            return new SdpMediaSection
            {
                Mid = mid,
                Kind = "audio",
                Direction = SdpDirection.Inactive,
                Codec = slot.LastCodec ?? RtpCodec.Opus()
            };
        }).ToList();
    }

    private void OnNegotiated(string localSdp)
    {
        LogLocalDescription(_sessionId, localSdp);

        bool isFirst;
        List<string> ready;

        lock (_lock)
        {
            isFirst = !_isDtlsPublished;
            _isDtlsPublished = true;

            ready = _roundSections
                .Select(section => section.InboundId)
                .OfType<string>()
                .Where(inboundId => _inbounds.ContainsKey(inboundId) && _readyInbounds.Add(inboundId))
                .ToList();
        }

        // The certificate does not change between rounds, so the SFU is told about it once
        if (isFirst && ParseDtlsParameters(localSdp) is { } dtls)
            LocalDtlsParametersReady?.Invoke(dtls);

        // The SFU starts the tracks only once it hears of them, so nothing may come in the way of this
        if (ready.Count > 0)
            InboundsReady?.Invoke(ready);

        try
        {
            // The receivers of the new sections exist only now
            ApplyVolumes();
        }
        finally
        {
            CompleteNegotiation();
        }
    }

    private void OnNegotiationFailed(string error)
    {
        LogNegotiationFailed(_sessionId, error);
        CompleteNegotiation();
    }

    private void CompleteNegotiation()
    {
        bool isPending;

        lock (_lock)
        {
            _isNegotiating = false;
            isPending = _isNegotiationPending;
            _isNegotiationPending = false;
        }

        if (isPending)
            Task.Run(Negotiate);
    }

    /// <summary>
    /// Our certificate fingerprint and DTLS role from the answer; the SFU checks the peer certificate
    /// against exactly this.
    /// </summary>
    private DtlsParameters? ParseDtlsParameters(string localSdp)
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
            LogLocalFingerprintMissing(_sessionId);
            return null;
        }

        // setup:active means we open the DTLS connection, which is the client role for the SFU
        var role = setup switch
        {
            "active" => "client",
            "passive" => "server",
            _ => "auto"
        };

        return new DtlsParameters(role, [new DtlsFingerprint(algorithm, fingerprint)]);
    }

    private void OnConnectionChanged(string state)
    {
        var mapped = state switch
        {
            "NEW" => WebRTCConnectionState.New,
            "CONNECTING" => WebRTCConnectionState.Connecting,
            "CONNECTED" => WebRTCConnectionState.Connected,
            "DISCONNECTED" => WebRTCConnectionState.Disconnected,
            "FAILED" => WebRTCConnectionState.Failed,
            _ => WebRTCConnectionState.Closed
        };

        LogConnectionState(_sessionId, state);

        if (mapped == ConnectionState)
            return;

        ConnectionState = mapped;

        if (mapped == WebRTCConnectionState.Connected)
            _connected.TrySetResult(true);
        else if (mapped is WebRTCConnectionState.Failed or WebRTCConnectionState.Closed)
            _connected.TrySetResult(false);

        if (mapped != WebRTCConnectionState.Connected)
            ReportEveryoneSilent();

        ConnectionStateChanged?.Invoke(mapped);
    }

    private void RequestLevels()
    {
        VoicePeer? peer;

        lock (_lock)
            peer = _peer;

        try
        {
            peer?.RequestLevels();
        }
        catch (Exception)
        {
            // Closed in the meantime
        }
    }

    /// <summary>
    /// Turns the levels of the sections into who speaks: the others by the sections of their voices,
    /// ourselves by the microphone.
    /// </summary>
    private void OnLevels(string levels)
    {
        var now = Environment.TickCount64;
        var holdUntil = now + (long)SpeakingHold.TotalMilliseconds;
        List<(int UserId, bool IsSpeaking)> changes = [];

        lock (_lock)
        {
            foreach (var item in levels.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = item.Split('=', 2);

                if (parts.Length != 2 || !double.TryParse(parts[1], CultureInfo.InvariantCulture, out var level) ||
                    level < SpeakingLevel)
                    continue;

                int? userId = parts[0] == "local"
                    ? _outbounds.Values.FirstOrDefault()?.UserId
                    : int.TryParse(parts[0], CultureInfo.InvariantCulture, out var mline) && mline < _slots.Count &&
                      _slots[mline].InboundId is { } inboundId && _inbounds.TryGetValue(inboundId, out var inbound) &&
                      inbound.SourceType == MediaSourceType.Microphone
                        ? inbound.UserId
                        : null;

                if (userId is not { } speaker)
                    continue;

                if (!_speakingUntil.ContainsKey(speaker))
                    changes.Add((speaker, true));

                _speakingUntil[speaker] = holdUntil;
            }

            foreach (var (userId, until) in _speakingUntil.ToList())
            {
                if (until > now)
                    continue;

                _speakingUntil.Remove(userId);
                changes.Add((userId, false));
            }
        }

        foreach (var (userId, isSpeaking) in changes)
            SpeakingChanged?.Invoke(userId, isSpeaking);
    }

    private void ReportEveryoneSilent()
    {
        List<int> speakers;

        lock (_lock)
        {
            speakers = _speakingUntil.Keys.ToList();
            _speakingUntil.Clear();
        }

        foreach (var userId in speakers)
            SpeakingChanged?.Invoke(userId, false);
    }

    /// <summary>
    /// Passes the calls of VoicePeer on; they come on the threads of libwebrtc, where an exception
    /// would tear the process down.
    /// </summary>
    private sealed class PeerListener(AndroidWebRTCTransport transport) : Java.Lang.Object, VoicePeer.IListener
    {
        public void OnConnectionChanged(string? state) => Run(() => transport.OnConnectionChanged(state ?? string.Empty));

        public void OnNegotiated(string? localSdp) => Run(() => transport.OnNegotiated(localSdp ?? string.Empty));

        public void OnFailed(string? error) => Run(() => transport.OnNegotiationFailed(error ?? "unknown"));

        public void OnLevels(string? levels) => Run(() => transport.OnLevels(levels ?? string.Empty));

        private void Run(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                transport.LogCallbackFailed(exception, transport._sessionId);
            }
        }
    }

    /// <summary>
    /// One m-section. A sending one is only reused for sending, a receiving one for receiving.
    /// </summary>
    private sealed class Slot
    {
        public bool IsSend { get; init; }

        /// <summary>The section of m-line 0 that only brings the transport up.</summary>
        public bool IsPlaceholder { get; init; }

        public string? InboundId { get; set; }

        public string? OutboundId { get; set; }

        /// <summary>
        /// The codec of the track the section carried last; an emptied section keeps offering it.
        /// </summary>
        public RtpCodec? LastCodec { get; set; }

        public bool Carries(string mimeType) =>
            LastCodec == null || string.IsNullOrEmpty(mimeType) ||
            LastCodec.EncodingName.Equals(RtpCodec.EncodingNameOf(mimeType), StringComparison.OrdinalIgnoreCase);
    }
}
