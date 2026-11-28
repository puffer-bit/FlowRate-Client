using System.Globalization;
using Client.Application.Ports;
using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Client.Core.Settings;
using Client.WebRTC.Sdp;
using Microsoft.Extensions.Logging;
using Shared.Enums.WebRTC;
using Shared.Models.Media;

namespace Client.LibWebRTC.Transport;

/// <summary>
/// The media of a hybrid session (voices, screens, cameras) over libwebrtc, through the
/// <see cref="IMediaPeer"/> of the platform. The description of the SFU is built out of its parameters (<see cref="SdpBuilder"/>);
/// libwebrtc answers it; the platform peer supplies capture and playback. Screens are watched and shown
/// where the platform hands the video out (<see cref="IEncodedVideoReceiver"/>) and takes it in
/// encoded (<see cref="IMediaPeer.CreateVideoSource"/>): <see cref="IVideoMedia"/> captures, encodes and
/// decodes them, libwebrtc only carries them. A peer that decodes the screens itself, as the one of
/// Android, hands their pictures out through <see cref="IMediaPeerListener.OnVideoFrame"/> instead.
/// </summary>
public sealed partial class LibWebRTCTransport : IWebRTCTransport
{
    public event Action<string>? VideoCaptureFailed;

    private readonly int _sessionId;
    private readonly bool _isPlaybackEnabled;
    private readonly Func<IMediaPeerListener, IMediaPeer> _createPeer;
    private readonly IVideoMedia? _video;
    private readonly IAudioMedia? _audio;
    private readonly IEncodedVideoReceiver? _videoReceiver;
    private readonly bool _isVideoDecodedByPeer;
    private readonly ILogger<LibWebRTCTransport> _logger;
    private readonly Lock _lock = new();

    // The m-sections by m-line index; never removed, only emptied and reused, which keeps every mid stable
    private readonly List<Slot> _slots = [];
    private readonly Dictionary<string, InboundTrack> _inbounds = [];
    private readonly Dictionary<string, IncomingVideoStream> _videoStreams = [];
    private readonly Dictionary<string, OutgoingVideo> _outgoingVideos = [];
    private readonly Dictionary<string, OutgoingSound> _outgoingSounds = [];
    private readonly Dictionary<string, OutboundTrack> _outbounds = [];
    private readonly HashSet<string> _readyInbounds = [];
    private readonly Dictionary<(int UserId, MediaSourceType SourceType), double> _volumes = [];
    private readonly TaskCompletionSource<bool> _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TransportStatsSampler _sampler = new();
    private readonly MediaPerfSampler _perfSampler = new();
    private readonly StatsSchedule _statsSchedule = new();
    private readonly SpeakingDetector _speaking = new();
    // Touched under _lock
    private readonly MutedSpeechDetector _mutedSpeech = new();

    private IMediaPeer? _peer;
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

    // The id of the transport-wide sequence number in the sections we send in, 0 while the SFU gave none
    private int _transportCcExtensionId;

    public event Action<DtlsParameters>? LocalDtlsParametersReady;
    public event Action<WebRTCConnectionState>? ConnectionStateChanged;
    public event Action<int, bool>? SpeakingChanged;
    public event Action<bool>? MutedSpeakingChanged;
    public event Action<int, MediaSourceType, RawVideoFrame>? VideoFrameReady;
    public event Action<int, MediaSourceType, VideoDecoding>? VideoDecodingChanged;

    public event Action<IReadOnlyList<string>>? InboundsReady;
    public event Action<TransportStatsSample>? StatsSampled;
    public event Action<MediaPerfReport>? PerfSampled;

    public WebRTCConnectionState ConnectionState { get; private set; } = WebRTCConnectionState.New;

    /// <param name="createPeer">Creates the peer of the platform, which reports to the listener it is given.</param>
    /// <param name="isPlaybackEnabled">
    /// Whether the voices are heard. libwebrtc plays every voice it receives, so without playback they
    /// are played silent.
    /// </param>
    /// <param name="video">Decodes the screens we watch; without it, or the receiver, none is watched.</param>
    /// <param name="videoReceiver">Hands out the encoded pictures of the screens libwebrtc receives.</param>
    /// <param name="audio">Captures the sound of the screens we show; without it they go out silent.</param>
    /// <param name="isVideoDecodedByPeer">
    /// The peer decodes the screens we watch itself and hands their pictures out, so neither the video
    /// nor the receiver is needed for them.
    /// </param>
    public LibWebRTCTransport(int sessionId, Func<IMediaPeerListener, IMediaPeer> createPeer, ILogger<LibWebRTCTransport> logger,
        bool isPlaybackEnabled = true, IVideoMedia? video = null, IEncodedVideoReceiver? videoReceiver = null,
        IAudioMedia? audio = null, bool isVideoDecodedByPeer = false)
    {
        _isVideoDecodedByPeer = isVideoDecodedByPeer;
        _sessionId = sessionId;
        _isPlaybackEnabled = isPlaybackEnabled;
        _createPeer = createPeer;
        _video = video;
        _audio = audio;
        _videoReceiver = videoReceiver;
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

            // Under the lock, so a Disconnect or Dispose coming at once finds it and stops it
            _statsTimer = new Timer(_ => RequestStats(), null, StatsSchedule.StatsInterval, StatsSchedule.StatsInterval);
        }

        return Negotiate();
    }

    public void Disconnect()
    {
        IMediaPeer? peer;

        lock (_lock)
        {
            peer = _peer;
            _peer = null;
        }

        Close(peer);
    }

    public void Dispose()
    {
        IMediaPeer? peer;
        List<IncomingVideoStream> streams;
        List<OutgoingVideo> outgoing;
        List<OutgoingSound> sounds;

        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            peer = _peer;
            _peer = null;
            streams = _videoStreams.Values.ToList();
            _videoStreams.Clear();
            outgoing = _outgoingVideos.Values.ToList();
            _outgoingVideos.Clear();
            sounds = _outgoingSounds.Values.ToList();
            _outgoingSounds.Clear();
        }

        Close(peer);

        foreach (var sound in sounds)
            sound.Dispose();

        foreach (var stream in streams)
            stream.Dispose();

        foreach (var video in outgoing)
            video.Dispose();
    }

    private void Close(IMediaPeer? peer)
    {
        Timer? statsTimer;

        lock (_lock)
        {
            statsTimer = _statsTimer;
            _statsTimer = null;
        }

        statsTimer?.Dispose();
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
        // A screen only where its pictures can be had and decoded
        VideoCodec? codec = null;

        if (inbound.MediaType == MediaType.Video)
        {
            if ((!_isVideoDecodedByPeer && (_video == null || _videoReceiver == null)) ||
                RtpCodec.VideoCodecOf(inbound.MimeType) is not { } videoCodec)
                return;

            codec = videoCodec;
        }
        else if (inbound.MediaType != MediaType.Audio)
        {
            return;
        }

        var kind = inbound.MediaType == MediaType.Video ? VideoKind : AudioKind;
        List<IncomingVideoStream> retired = [];

        lock (_lock)
        {
            // A picture has no volume, and must not reset the one of the sound of its screen
            if (inbound.MediaType == MediaType.Audio)
                _volumes[(inbound.UserId, inbound.SourceType)] = volume;

            if (_isDisposed || !_inbounds.TryAdd(inbound.Id, inbound))
                return;

            // A rejoin makes the SFU create a new consumer for a track we may still hold
            foreach (var stale in _inbounds.Values.Where(item => item.Id != inbound.Id && item.UserId == inbound.UserId &&
                                                               item.MediaType == inbound.MediaType &&
                                                               item.SourceType == inbound.SourceType).ToList())
                ReleaseLocked(stale.Id, retired);

            var slot = _slots.FirstOrDefault(item => !item.IsPlaceholder && !item.IsSend && item.InboundId == null &&
                                                     item.Kind == kind && item.Carries(inbound.MimeType, inbound.IsNackEnabled));

            if (slot == null)
                _slots.Add(slot = new Slot { Kind = kind });

            slot.InboundId = inbound.Id;

            if (codec is { } videoCodec && !_isVideoDecodedByPeer)
                _videoStreams[inbound.Id] = CreateVideoStream(inbound, videoCodec);
        }

        foreach (var stream in retired)
            stream.Dispose();

        LogInboundRegistered(_sessionId, inbound.MediaType, inbound.UserId, inbound.SSRC);
        Negotiate();
    }

    /// <summary>
    /// The stream of a screen we watch, decoded from its first picture on. Only inside the lock, with
    /// the video there.
    /// </summary>
    private IncomingVideoStream CreateVideoStream(InboundTrack inbound, VideoCodec codec)
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"{_sessionId}_{inbound.UserId}_{inbound.SSRC}");

        return new IncomingVideoStream(_videoReceiver!, inbound.SSRC, () =>
            {
                var decoder = _video!.CreateDecoder(codec, name, inbound.SourceType == MediaSourceType.Camera
                    ? VideoSettings.Current.ForCamera() : VideoSettings.Current);

                if (decoder != null)
                    LogDecodingVideo(_sessionId, inbound.UserId, codec);

                return decoder;
            },
            frame => VideoFrameReady?.Invoke(inbound.UserId, inbound.SourceType, frame),
            decoding => VideoDecodingChanged?.Invoke(inbound.UserId, inbound.SourceType, decoding),
            exception => LogVideoFrameFailed(exception, _sessionId, inbound.UserId));
    }

    public void UnregisterInbound(int userId, MediaType mediaType, MediaSourceType mediaSourceType) =>
        Release(inbound => inbound.UserId == userId && inbound.MediaType == mediaType &&
                           inbound.SourceType == mediaSourceType);

    public void UnregisterAllUserInbounds(int userId) => Release(inbound => inbound.UserId == userId);

    private void Release(Func<InboundTrack, bool> isReleased)
    {
        List<IncomingVideoStream> retired = [];

        lock (_lock)
        {
            var released = _inbounds.Values.Where(isReleased).Select(inbound => inbound.Id).ToList();

            if (released.Count == 0)
                return;

            foreach (var inboundId in released)
                ReleaseLocked(inboundId, retired);
        }

        foreach (var stream in retired)
            stream.Dispose();

        Negotiate();
    }

    /// <param name="retired">
    /// Receives the video stream of the track, to dispose outside the lock: stopping its decoder waits
    /// for the threads that hand its pictures on.
    /// </param>
    private void ReleaseLocked(string inboundId, List<IncomingVideoStream> retired)
    {
        _inbounds.Remove(inboundId);
        _readyInbounds.Remove(inboundId);

        if (_videoStreams.Remove(inboundId, out var stream))
            retired.Add(stream);

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
        IMediaPeer? peer;

        lock (_lock)
        {
            peer = _peer;
            volumes = [];

            for (var index = 0; index < _slots.Count; index++)
            {
                if (_slots[index].InboundId is { } inboundId && _inbounds.TryGetValue(inboundId, out var inbound) &&
                    inbound.MediaType == MediaType.Audio)
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

    /// <summary>Passes a picture the peer decoded on, as one of the screen its section carries now.</summary>
    private void OnVideoFrame(string mid, RawVideoFrame frame)
    {
        InboundTrack? inbound;

        lock (_lock)
        {
            inbound = int.TryParse(mid, CultureInfo.InvariantCulture, out var mline) && mline < _slots.Count &&
                      _slots[mline].InboundId is { } inboundId && _inbounds.TryGetValue(inboundId, out var track) &&
                      track.MediaType == MediaType.Video
                ? track
                : null;
        }

        // A picture still on its way from a screen that was released is dropped
        if (inbound != null)
            VideoFrameReady?.Invoke(inbound.UserId, inbound.SourceType, frame);
    }

    // Only inside the lock
    private InboundTrack? FindInbound(uint ssrc) => _inbounds.Values.FirstOrDefault(inbound => inbound.SSRC == ssrc);

    /// <summary>
    /// Passes the calls of the peer on; they come on the threads of libwebrtc, where an exception
    /// would tear the process down.
    /// </summary>
    private sealed class PeerListener(LibWebRTCTransport transport) : IMediaPeerListener
    {
        public void OnConnectionChanged(WebRTCConnectionState state) => Run(() => transport.OnConnectionChanged(state));

        public void OnNegotiated(string localSdp) => Run(() => transport.OnNegotiated(localSdp));

        public void OnFailed(string error) => Run(() => transport.OnNegotiationFailed(error));

        public void OnStats(TransportStats stats) => Run(() => transport.OnStats(stats));

        public void OnVideoFrame(string mid, RawVideoFrame frame) => Run(() => transport.OnVideoFrame(mid, frame));

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
}
