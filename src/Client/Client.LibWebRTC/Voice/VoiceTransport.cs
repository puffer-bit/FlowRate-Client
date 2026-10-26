using System.Globalization;
using Client.Application.Ports;
using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Client.Core.Settings;
using Client.WebRTC.Sdp;
using Microsoft.Extensions.Logging;
using Shared.Enums.WebRTC;
using Shared.Models.Media;

namespace Client.LibWebRTC.Voice;

/// <summary>
/// Voice of a hybrid session over libwebrtc, through the <see cref="IVoicePeer"/> of the platform.
/// The description of the SFU is built the way the GStreamer transport builds it for webrtcbin;
/// libwebrtc answers it, captures the microphone and plays the voices. Screens are neither shown
/// nor watched.
/// </summary>
public sealed partial class VoiceTransport : IWebRTCTransport
{
    // How often the statistics are read: often enough for the speaking indicator, which goes by
    // the levels in them
    private static readonly TimeSpan StatsInterval = TimeSpan.FromMilliseconds(200);

    // The audio level, 0..1, above which a voice counts as speech, and how long it is held after it falls
    private const double SpeakingLevel = 0.02;
    private static readonly TimeSpan SpeakingHold = TimeSpan.FromMilliseconds(400);

    // The bitrate of the voice, the same as the one of the GStreamer transport
    private const int VoiceBitrate = 64000;

    private readonly int _sessionId;
    private readonly bool _isPlaybackEnabled;
    private readonly Func<IVoicePeerListener, IVoicePeer> _createPeer;
    private readonly ILogger<VoiceTransport> _logger;
    private readonly Lock _lock = new();

    // The m-sections by m-line index; never removed, only emptied and reused, which keeps every mid stable
    private readonly List<Slot> _slots = [];
    private readonly Dictionary<string, InboundTrack> _inbounds = [];
    private readonly Dictionary<string, OutboundTrack> _outbounds = [];
    private readonly HashSet<string> _readyInbounds = [];
    private readonly Dictionary<(int UserId, MediaSourceType SourceType), double> _volumes = [];
    private readonly Dictionary<int, long> _speakingUntil = [];
    private readonly TaskCompletionSource<bool> _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly VoiceStatsSampler _sampler = new();

    private IVoicePeer? _peer;
    private TransportParameters? _transportParameters;
    private Timer? _statsTimer;
    private bool _isVoiceMuted;

    // libwebrtc measures the microphone before the track that mutes it, so a muted one still reads loud
    private bool _isMicrophoneMuted;
    private bool _isDisposed;
    private bool _isNegotiating;
    private bool _isNegotiationPending;
    private bool _isDtlsPublished;
    private IReadOnlyList<SdpMediaSection> _roundSections = [];

    public event Action<DtlsParameters>? LocalDtlsParametersReady;
    public event Action<WebRTCConnectionState>? ConnectionStateChanged;
    public event Action<int, bool>? SpeakingChanged;
    // No screen is watched, so no frame ever comes
    public event Action<int, MediaSourceType, RawVideoFrame>? VideoFrameReady
    {
        add { }
        remove { }
    }

    public event Action<IReadOnlyList<string>>? InboundsReady;
    public event Action<TransportStatsSample>? StatsSampled;

    public WebRTCConnectionState ConnectionState { get; private set; } = WebRTCConnectionState.New;

    /// <param name="createPeer">Creates the peer of the platform, which reports to the listener it is given.</param>
    /// <param name="isPlaybackEnabled">
    /// Whether the voices are heard. libwebrtc plays every voice it receives, so without playback they
    /// are played silent.
    /// </param>
    public VoiceTransport(int sessionId, Func<IVoicePeerListener, IVoicePeer> createPeer, ILogger<VoiceTransport> logger,
        bool isPlaybackEnabled = true)
    {
        _sessionId = sessionId;
        _isPlaybackEnabled = isPlaybackEnabled;
        _createPeer = createPeer;
        _logger = logger;
    }

    public bool Connect(TransportParameters transportParameters)
    {
        lock (_lock)
        {
            if (_isDisposed || _peer != null)
                return false;

            _transportParameters = transportParameters;
            _peer = _createPeer(new PeerListener(this));
        }

        _statsTimer = new Timer(_ => RequestStats(), null, StatsInterval, StatsInterval);

        return Negotiate();
    }

    public void Disconnect()
    {
        IVoicePeer? peer;

        lock (_lock)
        {
            peer = _peer;
            _peer = null;
        }

        Close(peer);
    }

    public void Dispose()
    {
        IVoicePeer? peer;

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

    private void Close(IVoicePeer? peer)
    {
        _statsTimer?.Dispose();
        _statsTimer = null;
        _connected.TrySetResult(false);

        if (peer == null)
            return;

        peer.Dispose();
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
        IVoicePeer? peer;

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
        !_isPlaybackEnabled || (_isVoiceMuted && inbound.SourceType == MediaSourceType.Microphone)
            ? 0
            : _volumes.GetValueOrDefault((inbound.UserId, inbound.SourceType), 1.0);

    public bool StartOutbound(OutboundTrack outbound, AudioInput input, double volume, VoiceGateOptions? voiceGate,
        NoiseSuppressionLevel noiseSuppression, bool isEchoCancellationEnabled)
    {
        // Only the microphone: the sound of a screen goes with the screen
        if (outbound.MediaType != MediaType.Audio || outbound.SourceType != MediaSourceType.Microphone)
            return false;

        IVoicePeer? peer;

        lock (_lock)
        {
            peer = _peer;

            if (peer == null || _outbounds.ContainsKey(outbound.Id))
                return false;
        }

        // Never inside the lock: the peer waits for the threads of libwebrtc, and its callbacks on
        // them wait for the lock
        if (!peer.SetMicrophone(input, noiseSuppression, isEchoCancellationEnabled))
            return false;

        peer.SetMicrophoneVolume(volume);
        peer.SetVoiceGate(voiceGate);
        peer.SetMicrophoneEnabled(true);

        lock (_lock)
        {
            if (_peer != peer || !_outbounds.TryAdd(outbound.Id, outbound))
                return false;

            var slot = _slots.FirstOrDefault(item => item.IsSend && item.OutboundId == null && item.Carries(outbound.MimeType));

            if (slot == null)
                _slots.Add(slot = new Slot { IsSend = true });

            slot.OutboundId = outbound.Id;
            _isMicrophoneMuted = false;
        }

        LogOutboundStarted(_sessionId, outbound.SSRC);

        return Negotiate();
    }

    public bool StartOutbound(OutboundTrack outbound, VideoInput input, ScreenShareSettings settings) => false;

    public void StopOutbound(string outboundId)
    {
        IVoicePeer? peer;
        bool isLast;

        lock (_lock)
        {
            if (!_outbounds.Remove(outboundId))
                return;

            foreach (var slot in _slots.Where(slot => slot.OutboundId == outboundId))
                slot.OutboundId = null;

            peer = _peer;
            isLast = _outbounds.Count == 0;
        }

        if (isLast)
            peer?.SetVoiceGate(null);

        Negotiate();
    }

    public bool SetOutboundMuted(string outboundId, bool isMuted)
    {
        if (FindOutboundPeer(outboundId) is not { } peer)
            return false;

        lock (_lock)
            _isMicrophoneMuted = isMuted;

        peer.SetMicrophoneEnabled(!isMuted);
        return true;
    }

    public void SetOutboundVolume(string outboundId, double volume) =>
        FindOutboundPeer(outboundId)?.SetMicrophoneVolume(volume);

    public void SetOutboundInput(string outboundId, AudioInput input, NoiseSuppressionLevel noiseSuppression,
        bool isEchoCancellationEnabled) =>
        FindOutboundPeer(outboundId)?.SetMicrophone(input, noiseSuppression, isEchoCancellationEnabled);

    public void SetVoiceGate(string outboundId, VoiceGateOptions? voiceGate) =>
        FindOutboundPeer(outboundId)?.SetVoiceGate(voiceGate);

    /// <summary>The peer, while it sends the track.</summary>
    private IVoicePeer? FindOutboundPeer(string outboundId)
    {
        lock (_lock)
            return _outbounds.ContainsKey(outboundId) ? _peer : null;
    }

    /// <summary>
    /// Runs an offer/answer round over the current sections. A round asked for while another one runs
    /// is coalesced into a single follow-up.
    /// </summary>
    private bool Negotiate()
    {
        IVoicePeer peer;
        string offer;
        List<string> sendMids;
        List<uint> sendSsrcs;

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
            sendMids = sends.Select(item => item.section.Mid).ToList();
            sendSsrcs = sends.Select(item => _outbounds[item.Outbound!].SSRC).ToList();
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
                    slot.LastCodec = SendCodec(outbound);
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

    /// <summary>
    /// The codec of a section we send in. libwebrtc encodes by what the receiver asks for in its
    /// description, and the receiver here is the SFU, described by us: so the format parameters of
    /// the section are what sets FEC, DTX and the bitrate of our voice.
    /// </summary>
    private static RtpCodec SendCodec(OutboundTrack outbound) => RtpCodec.FromOutbound(outbound) with
    {
        FormatParameters = string.Create(CultureInfo.InvariantCulture,
            $"minptime=10;useinbandfec={(outbound.IsFecEnabled ? 1 : 0)};usedtx={(outbound.IsDtxEnabled ? 1 : 0)};maxaveragebitrate={VoiceBitrate}")
    };

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

    private void OnConnectionChanged(WebRTCConnectionState state)
    {
        LogConnectionState(_sessionId, state);

        if (state == ConnectionState)
            return;

        ConnectionState = state;

        if (state == WebRTCConnectionState.Connected)
            _connected.TrySetResult(true);
        else if (state is WebRTCConnectionState.Failed or WebRTCConnectionState.Closed)
            _connected.TrySetResult(false);

        if (state != WebRTCConnectionState.Connected)
            ReportEveryoneSilent();

        ConnectionStateChanged?.Invoke(state);
    }

    private void RequestStats()
    {
        IVoicePeer? peer;

        lock (_lock)
            peer = _peer;

        try
        {
            peer?.RequestStats();
        }
        catch (Exception)
        {
            // Closed in the meantime
        }
    }

    private void OnStats(VoiceStats stats)
    {
        UpdateSpeaking(stats.Levels);

        TransportStatsSample? sample;

        lock (_lock)
        {
            sample = stats.Streams is { } streams
                ? _sampler.Read(streams, FindInbound, ssrc => _outbounds.Values.Any(outbound => outbound.SSRC == ssrc))
                : null;
        }

        if (sample is { } reading)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                LogStatsSample(_sessionId, reading.RoundTripMs ?? 0, reading.InboundLossPercent, reading.OutboundLossPercent,
                    reading.IsOverTcp, string.Join(", ", reading.Inbound?.Select(stream =>
                        $"user {stream.UserId}: {stream.Packets} packets, jitter {stream.JitterMs:F0} ms") ?? []));
            StatsSampled?.Invoke(reading);
        }
    }

    // Only inside the lock
    private InboundTrack? FindInbound(uint ssrc) => _inbounds.Values.FirstOrDefault(inbound => inbound.SSRC == ssrc);

    /// <summary>
    /// Turns the levels of the sections into who speaks: the others by the sections of their voices,
    /// ourselves by the microphone.
    /// </summary>
    private void UpdateSpeaking(IReadOnlyList<VoiceLevel> levels)
    {
        var now = Environment.TickCount64;
        var holdUntil = now + (long)SpeakingHold.TotalMilliseconds;
        List<(int UserId, bool IsSpeaking)> changes = [];

        lock (_lock)
        {
            foreach (var (mid, level) in levels)
            {
                if (level < SpeakingLevel)
                    continue;

                int? userId = mid == null
                    ? _isMicrophoneMuted ? null : _outbounds.Values.FirstOrDefault()?.UserId
                    : int.TryParse(mid, CultureInfo.InvariantCulture, out var mline) && mline < _slots.Count &&
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
    /// Passes the calls of the peer on; they come on the threads of libwebrtc, where an exception
    /// would tear the process down.
    /// </summary>
    private sealed class PeerListener(VoiceTransport transport) : IVoicePeerListener
    {
        public void OnConnectionChanged(WebRTCConnectionState state) => Run(() => transport.OnConnectionChanged(state));

        public void OnNegotiated(string localSdp) => Run(() => transport.OnNegotiated(localSdp));

        public void OnFailed(string error) => Run(() => transport.OnNegotiationFailed(error));

        public void OnStats(VoiceStats stats) => Run(() => transport.OnStats(stats));

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
