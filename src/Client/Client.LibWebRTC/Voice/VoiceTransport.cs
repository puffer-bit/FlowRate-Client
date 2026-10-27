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
/// libwebrtc answers it, captures the microphone and plays the voices. Screens are watched and shown
/// where the platform hands the video out (<see cref="IEncodedVideoReceiver"/>) and takes it in
/// encoded (<see cref="IVoicePeer.CreateVideoSource"/>): <see cref="IVideoMedia"/> captures, encodes and
/// decodes them, libwebrtc only carries them. A peer that decodes the screens itself, as the one of
/// Android, hands their pictures out through <see cref="IVoicePeerListener.OnVideoFrame"/> instead.
/// </summary>
public sealed partial class VoiceTransport : IWebRTCTransport
{
    // How often the statistics are read: often enough for the speaking indicator, which goes by
    // the levels in them
    private static readonly TimeSpan StatsInterval = TimeSpan.FromMilliseconds(200);

    // As often as the GStreamer transport tells what its gate did
    private static readonly TimeSpan GateSummaryInterval = TimeSpan.FromMinutes(1);

    // The audio level, 0..1, above which a voice counts as speech, and how long it is held after it falls
    private const double SpeakingLevel = 0.02;
    private static readonly TimeSpan SpeakingHold = TimeSpan.FromMilliseconds(400);

    // The bitrate of the voice, the same as the one of the GStreamer transport
    private const int VoiceBitrate = 64000;

    // The sound of a screen, stereo at the bitrate of the GStreamer transport, which keeps music clean
    private const int ScreenSoundBitrate = 128000;

    private const string TransportCcFeedback = "transport-cc";

    private readonly int _sessionId;
    private readonly bool _isPlaybackEnabled;
    private readonly Func<IVoicePeerListener, IVoicePeer> _createPeer;
    private readonly IVideoMedia? _video;
    private readonly IAudioMedia? _audio;
    private readonly IEncodedVideoReceiver? _videoReceiver;
    private readonly bool _isVideoDecodedByPeer;
    private readonly ILogger<VoiceTransport> _logger;
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
    private readonly Dictionary<int, long> _speakingUntil = [];
    private readonly TaskCompletionSource<bool> _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly VoiceStatsSampler _sampler = new();

    private IVoicePeer? _peer;
    private TransportParameters? _transportParameters;
    private Timer? _statsTimer;
    private long _gateSummaryTaken = Environment.TickCount64;
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
    public event Action<int, MediaSourceType, RawVideoFrame>? VideoFrameReady;

    public event Action<IReadOnlyList<string>>? InboundsReady;
    public event Action<TransportStatsSample>? StatsSampled;

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
    public VoiceTransport(int sessionId, Func<IVoicePeerListener, IVoicePeer> createPeer, ILogger<VoiceTransport> logger,
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
        // A screen only where its pictures can be had and decoded
        VideoCodec? codec = null;

        if (inbound.MediaType == MediaType.Video)
        {
            if ((!_isVideoDecodedByPeer && (_video == null || _videoReceiver == null)) ||
                VideoCodecOf(inbound.MimeType) is not { } videoCodec)
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
                                                     item.Kind == kind && item.Carries(inbound.MimeType));

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
                var decoder = _video!.CreateDecoder(codec, name);

                if (decoder != null)
                    LogDecodingVideo(_sessionId, inbound.UserId, codec);

                return decoder;
            },
            frame => VideoFrameReady?.Invoke(inbound.UserId, inbound.SourceType, frame),
            exception => LogVideoFrameFailed(exception, _sessionId, inbound.UserId));
    }

    /// <summary>The codec of a screen we can decode, by the MIME type the SFU gives its track.</summary>
    private static VideoCodec? VideoCodecOf(string mimeType) =>
        RtpCodec.EncodingNameOf(mimeType).ToUpperInvariant() switch
        {
            "H264" => VideoCodec.H264,
            "VP8" => VideoCodec.VP8,
            _ => null
        };

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
        IVoicePeer? peer;

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

    public bool StartOutbound(OutboundTrack outbound, AudioInput input, double volume, VoiceGateOptions? voiceGate,
        NoiseSuppressionLevel noiseSuppression, bool isEchoCancellationEnabled, bool isAutoGainControlEnabled)
    {
        if (outbound.MediaType != MediaType.Audio)
            return false;

        // A screen is heard as it plays: nothing of the microphone, its gate or its volume applies
        if (outbound.SourceType == MediaSourceType.Screen)
            return StartSound(outbound, input, volume);

        if (outbound.SourceType != MediaSourceType.Microphone)
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
        if (!peer.SetMicrophone(input, noiseSuppression, isEchoCancellationEnabled, isAutoGainControlEnabled))
            return false;

        peer.SetMicrophoneVolume(volume);
        peer.SetVoiceGate(voiceGate);
        peer.SetMicrophoneEnabled(true);

        lock (_lock)
        {
            if (_peer != peer || !_outbounds.TryAdd(outbound.Id, outbound))
                return false;

            ClaimSendingSlot(outbound, AudioKind);
            _isMicrophoneMuted = false;
        }

        LogOutboundStarted(_sessionId, outbound.SSRC);

        return Negotiate();
    }

    /// <summary>
    /// Sends the sound of a shown screen: <see cref="IAudioMedia"/> captures it, and libwebrtc encodes
    /// it into a section of its own, untouched by the processing of the microphone.
    /// </summary>
    private bool StartSound(OutboundTrack outbound, AudioInput input, double volume)
    {
        IVoicePeer? peer;

        lock (_lock)
        {
            peer = _peer;

            if (peer == null || _audio == null)
                return false;

            if (_outbounds.ContainsKey(outbound.Id))
                return true;
        }

        var capture = _audio.CreateCapture(input, _sessionId.ToString(CultureInfo.InvariantCulture));

        if (capture == null)
        {
            LogSoundCaptureFailed(_sessionId, input);
            return false;
        }

        if (peer.CreateAudioSource() is not { } source)
        {
            capture.Dispose();
            return false;
        }

        var sound = new OutgoingSound(capture, source) { Volume = volume };

        lock (_lock)
        {
            if (_peer != peer || !_outbounds.TryAdd(outbound.Id, outbound))
            {
                sound.Dispose();
                return false;
            }

            _outgoingSounds[outbound.Id] = sound;
            ClaimSendingSlot(outbound, AudioKind);
        }

        if (!sound.Start())
        {
            StopOutbound(outbound.Id);
            return false;
        }

        LogScreenSoundStarted(_sessionId, outbound.SSRC);

        return Negotiate();
    }

    /// <summary>
    /// Shows a screen: <see cref="IVideoMedia"/> captures and encodes it, libwebrtc sends the pictures.
    /// What is sent also comes back through <see cref="VideoFrameReady"/>, as our own screen is not
    /// received from the SFU.
    /// </summary>
    public bool StartOutbound(OutboundTrack outbound, VideoInput input, ScreenShareSettings settings)
    {
        if (outbound.MediaType != MediaType.Video || _video == null || VideoCodecOf(outbound.MimeType) is not { } codec)
            return false;

        IVoicePeer? peer;

        lock (_lock)
        {
            peer = _peer;

            if (peer == null)
                return false;

            if (_outbounds.ContainsKey(outbound.Id))
                return true;
        }

        var capture = _video.CreateCapture(input, settings, codec, _sessionId.ToString(CultureInfo.InvariantCulture));

        if (capture == null)
        {
            LogCaptureFailed(_sessionId, input);
            return false;
        }

        var video = new OutgoingVideo(capture, settings, kbps => LogScreenBitrate(_sessionId, kbps));

        // The size is what libwebrtc takes the stream for: the one of the setting, as wide as a usual screen
        var height = settings.MaxHeight;
        var width = (height * 16 / 9 + 1) & ~1;

        // Never inside the lock, like the microphone
        if (peer.CreateVideoSource(width, height, settings.Framerate, video) is not { } source)
        {
            capture.Dispose();
            LogCaptureFailed(_sessionId, input);
            return false;
        }

        video.Source = source;
        capture.EncodedFrameReady += source.Push;
        capture.PreviewFrameReady += frame => VideoFrameReady?.Invoke(outbound.UserId, outbound.SourceType, frame);

        lock (_lock)
        {
            if (_peer != peer || !_outbounds.TryAdd(outbound.Id, outbound))
            {
                video.Dispose();
                return false;
            }

            _outgoingVideos[outbound.Id] = video;
            ClaimSendingSlot(outbound, VideoKind);
        }

        // The pictures before the round that puts the source into its section go nowhere; libwebrtc
        // asks for a keyframe with its first one
        if (!capture.Start())
        {
            StopOutbound(outbound.Id);
            LogCaptureFailed(_sessionId, input);
            return false;
        }

        LogScreenStarted(_sessionId, outbound.SSRC, capture.Description);

        return Negotiate();
    }

    /// <summary>Gives a track a section we send in, an emptied one of its kind and codec where there is one. Only inside the lock.</summary>
    private void ClaimSendingSlot(OutboundTrack outbound, string kind)
    {
        var slot = _slots.FirstOrDefault(item => item.IsSend && item.OutboundId == null && item.Kind == kind &&
                                                 item.Carries(outbound.MimeType));

        if (slot == null)
            _slots.Add(slot = new Slot { IsSend = true, Kind = kind });

        slot.OutboundId = outbound.Id;

        // The same for every track of the server; an extension once negotiated keeps its id
        if (_transportCcExtensionId <= 0)
            _transportCcExtensionId = outbound.TransportCcExtensionId;
        slot.PausedSsrc = null;
        slot.PausedRtxSsrc = 0;
    }

    public void StopOutbound(string outboundId)
    {
        IVoicePeer? peer;
        OutgoingVideo? video;
        OutgoingSound? sound;
        bool isMicrophone;
        bool isLastMicrophone;

        lock (_lock)
        {
            if (!_outbounds.Remove(outboundId, out var outbound))
                return;

            _outgoingVideos.Remove(outboundId, out video);
            _outgoingSounds.Remove(outboundId, out sound);

            foreach (var slot in _slots.Where(slot => slot.OutboundId == outboundId))
            {
                slot.OutboundId = null;

                // The SFU only pauses a screen that stops, see Slot.PausedSsrc
                if (video != null)
                {
                    slot.PausedSsrc = outbound.SSRC;
                    slot.PausedRtxSsrc = RtxSsrcOf(outbound);
                }
            }

            peer = _peer;
            isMicrophone = IsMicrophone(outbound);
            isLastMicrophone = !_outbounds.Values.Any(IsMicrophone);
        }

        // Outside the lock: stopping the capture waits for its threads
        video?.Dispose();
        sound?.Dispose();

        if (isMicrophone && isLastMicrophone)
            peer?.SetVoiceGate(null);

        Negotiate();
    }

    public bool SetOutboundMuted(string outboundId, bool isMuted)
    {
        lock (_lock)
        {
            if (_outgoingSounds.TryGetValue(outboundId, out var sound))
            {
                sound.IsMuted = isMuted;
                return true;
            }
        }

        if (FindOutboundPeer(outboundId) is not { } peer)
            return false;

        lock (_lock)
            _isMicrophoneMuted = isMuted;

        peer.SetMicrophoneEnabled(!isMuted);
        return true;
    }

    public void SetOutboundVolume(string outboundId, double volume)
    {
        lock (_lock)
        {
            if (_outgoingSounds.TryGetValue(outboundId, out var sound))
            {
                sound.Volume = volume;
                return;
            }
        }

        FindOutboundPeer(outboundId)?.SetMicrophoneVolume(volume);
    }

    public void SetOutboundInput(string outboundId, AudioInput input, NoiseSuppressionLevel noiseSuppression,
        bool isEchoCancellationEnabled, bool isAutoGainControlEnabled)
    {
        OutgoingSound? sound;

        lock (_lock)
            sound = _outgoingSounds.GetValueOrDefault(outboundId);

        if (sound == null)
        {
            FindOutboundPeer(outboundId)?.SetMicrophone(input, noiseSuppression, isEchoCancellationEnabled,
                isAutoGainControlEnabled);
            return;
        }

        // Outside the lock: stopping the old capture waits for its thread
        if (_audio?.CreateCapture(input, _sessionId.ToString(CultureInfo.InvariantCulture)) is { } capture)
            sound.Replace(capture);
        else
            LogSoundCaptureFailed(_sessionId, input);
    }

    public void SetVoiceGate(string outboundId, VoiceGateOptions? voiceGate) =>
        FindOutboundPeer(outboundId)?.SetVoiceGate(voiceGate);

    /// <summary>The peer, while it sends the track of the microphone.</summary>
    private IVoicePeer? FindOutboundPeer(string outboundId)
    {
        lock (_lock)
            return _outbounds.TryGetValue(outboundId, out var outbound) && IsMicrophone(outbound) ? _peer : null;
    }

    private static bool IsMicrophone(OutboundTrack outbound) =>
        outbound.MediaType == MediaType.Audio && outbound.SourceType == MediaSourceType.Microphone;

    /// <summary>
    /// Runs an offer/answer round over the current sections. A round asked for while another one runs
    /// is coalesced into a single follow-up.
    /// </summary>
    private bool Negotiate()
    {
        IVoicePeer peer;
        string offer;
        List<SendSection> sends;

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

            sends = sections
                .Select((section, index) => (section.Mid, Slot: _slots[index]))
                .Select(item => item.Slot.OutboundId != null && _outbounds.TryGetValue(item.Slot.OutboundId, out var outbound)
                    ? _outgoingVideos.TryGetValue(outbound.Id, out var video)
                        ? new SendSection(item.Mid, outbound.SSRC, video.Source, RtxSsrcOf(outbound))
                        : new SendSection(item.Mid, outbound.SSRC, Audio: _outgoingSounds.GetValueOrDefault(outbound.Id)?.Source)
                    : item.Slot.PausedSsrc is { } paused ? new SendSection(item.Mid, paused, RtxSsrc: item.Slot.PausedRtxSsrc) : null)
                .OfType<SendSection>()
                .ToList();

            _roundSections = sections;
            offer = SdpBuilder.Build(_transportParameters, sections);
        }

        LogRemoteDescription(_sessionId, offer);

        try
        {
            peer.Negotiate(offer, sends);
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
                return new SdpMediaSection { Mid = mid, Kind = AudioKind, Direction = SdpDirection.SendOnly, Codec = RtpCodec.Opus() };

            if (slot.IsSend)
            {
                if (slot.OutboundId != null && _outbounds.TryGetValue(slot.OutboundId, out var outbound))
                {
                    slot.LastCodec = _outgoingVideos.TryGetValue(outbound.Id, out var video)
                        ? VideoSendCodec(outbound, video)
                        : SendCodec(outbound);
                    return SendingSection(mid, slot.Kind, slot.LastCodec);
                }

                if (slot.PausedSsrc != null && slot.LastCodec != null)
                    return SendingSection(mid, slot.Kind, slot.LastCodec);
            }
            else if (slot.InboundId != null && _inbounds.TryGetValue(slot.InboundId, out var inbound))
            {
                slot.LastCodec = RtpCodec.FromInbound(inbound);
                return new SdpMediaSection
                {
                    Mid = mid,
                    Kind = slot.Kind,
                    Direction = SdpDirection.SendOnly,
                    Codec = slot.LastCodec,
                    SSRC = inbound.SSRC,
                    InboundId = inbound.Id
                };
            }

            // An m-line keeps its kind for good, so an emptied section of a picture stays a video one
            return new SdpMediaSection
            {
                Mid = mid,
                Kind = slot.Kind,
                Direction = SdpDirection.Inactive,
                Codec = slot.LastCodec ?? (slot.Kind == VideoKind ? RtpCodec.H264() : RtpCodec.Opus())
            };
        }).ToList();
    }

    /// <summary>
    /// A section we send in. Where the SFU answers with transport-cc feedback, every packet carries its
    /// number on the transport, and libwebrtc estimates the bandwidth by the delays the feedback
    /// reports. Where it does not, the section offers none: libwebrtc would take the missing feedback
    /// for a network that delivers nothing, and lower the bitrate of a screen to its least.
    /// </summary>
    private SdpMediaSection SendingSection(string mid, string kind, RtpCodec codec) => _transportCcExtensionId <= 0
        ? new SdpMediaSection { Mid = mid, Kind = kind, Direction = SdpDirection.RecvOnly, Codec = codec }
        : new SdpMediaSection
        {
            Mid = mid,
            Kind = kind,
            Direction = SdpDirection.RecvOnly,
            Codec = codec.RtcpFeedback.Contains(TransportCcFeedback)
                ? codec
                : codec with { RtcpFeedback = [..codec.RtcpFeedback, TransportCcFeedback] },
            HeaderExtensions = [new SdpHeaderExtension(_transportCcExtensionId, SdpHeaderExtension.TransportWideCcUri)]
        };

    /// <summary>
    /// The codec of a section we send in. libwebrtc encodes by what the receiver asks for in its
    /// description, and the receiver here is the SFU, described by us: so the format parameters of
    /// the section are what sets FEC, DTX and the bitrate of our voice.
    /// </summary>
    private static RtpCodec SendCodec(OutboundTrack outbound) => RtpCodec.FromOutbound(outbound) with
    {
        // The sound of a screen is stereo, and never has DTX: a quiet passage of a film is not a pause
        FormatParameters = outbound.SourceType == MediaSourceType.Screen
            ? string.Create(CultureInfo.InvariantCulture,
                $"minptime=10;useinbandfec={(outbound.IsFecEnabled ? 1 : 0)};usedtx=0;stereo=1;sprop-stereo=1;maxaveragebitrate={ScreenSoundBitrate}")
            : string.Create(CultureInfo.InvariantCulture,
                $"minptime=10;useinbandfec={(outbound.IsFecEnabled ? 1 : 0)};usedtx={(outbound.IsDtxEnabled ? 1 : 0)};maxaveragebitrate={VoiceBitrate}")
    };

    /// <summary>
    /// The codec of a section we send a screen in. libwebrtc takes the bitrates of the remote
    /// description as the bounds of its estimate: it starts at the one the user set, never goes above
    /// it, and goes down when the network loses packets.
    /// </summary>
    private static RtpCodec VideoSendCodec(OutboundTrack outbound, OutgoingVideo video)
    {
        var codec = RtpCodec.FromOutbound(outbound);
        var bitrates = string.Create(CultureInfo.InvariantCulture,
            $"x-google-start-bitrate={video.Settings.EffectiveBitrateKbps};x-google-max-bitrate={video.Settings.EffectiveBitrateKbps};x-google-min-bitrate={video.MinKbps}");

        return codec with
        {
            FormatParameters = string.IsNullOrEmpty(codec.FormatParameters) ? bitrates : $"{codec.FormatParameters};{bitrates}",
            RtxPayloadType = RtxSsrcOf(outbound) != 0 ? outbound.RtxPayloadType : null
        };
    }

    /// <summary>
    /// The SSRC libwebrtc resends the lost packets of a screen on, as RTX: the SFU asks for them only
    /// where it gave the producer an RTX stream, and takes them only there. 0 where it gave none.
    /// </summary>
    private static uint RtxSsrcOf(OutboundTrack outbound) =>
        outbound.RtxPayloadType > 0 && outbound.RtxSSRC != 0 ? outbound.RtxSSRC : 0;

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

            if (_logger.IsEnabled(LogLevel.Debug) && stats.Streams is { } counters)
                LogStatsCounters(_sessionId, counters.AvailableOutgoingKbps ?? 0,
                    string.Join(", ", counters.Outbound.Select(stream =>
                        $"{stream.Ssrc}: {stream.Packets} packets, target {stream.TargetKbps ?? 0:F0} kbit/s, " +
                        $"{stream.Nacks} nacks, {stream.Retransmitted} resent, {stream.KeyframeRequests} plis")),
                    string.Join(", ", counters.Inbound.Select(stream =>
                        $"{stream.Ssrc}: {stream.Packets} packets, {stream.Lost} lost, {stream.Nacks} nacks, " +
                        $"{stream.KeyframeRequests} plis, {stream.Freezes} freezes")));

            StatsSampled?.Invoke(reading);
        }

        LogGateSummary();
    }

    // The microphone of libwebrtc is one for the process, and so is its gate
    private void LogGateSummary()
    {
        var now = Environment.TickCount64;

        if (now - _gateSummaryTaken < GateSummaryInterval.TotalMilliseconds)
            return;

        _gateSummaryTaken = now;

        if (Native.NativeVoiceGate.TakeSummary() is { } summary)
            LogVoiceGateSummary(_sessionId, summary.ThresholdDb, summary.EffectiveThresholdDb, summary.NoiseFloorDb,
                summary.OpenPercent, summary.Openings, summary.LoudestDb, summary.AverageOpenPeakDb);
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

    private const string AudioKind = "audio";
    private const string VideoKind = "video";

    /// <summary>
    /// One m-section. A sending one is only reused for sending, a receiving one for receiving, and
    /// each only for its kind.
    /// </summary>
    private sealed class Slot
    {
        public bool IsSend { get; init; }

        /// <summary>The kind of the m-line, audio or video.</summary>
        public string Kind { get; init; } = AudioKind;

        /// <summary>The section of m-line 0 that only brings the transport up.</summary>
        public bool IsPlaceholder { get; init; }

        public string? InboundId { get; set; }

        public string? OutboundId { get; set; }

        /// <summary>
        /// The codec of the track the section carried last; an emptied section keeps offering it.
        /// </summary>
        public RtpCodec? LastCodec { get; set; }

        /// <summary>
        /// The SSRC of the screen the section sent until it stopped, which the SFU only pauses. The
        /// section goes on sending, nothing: emptied, libwebrtc would end the stream with an RTCP BYE
        /// and start another one when the screen is back. Kept, the stream goes on with the next
        /// sequence number, as the SFU wants of a paused producer: it drops the packets after a jump,
        /// and those are the keyframe every viewer waits for.
        /// </summary>
        public uint? PausedSsrc { get; set; }

        /// <summary>The SSRC of the RTX stream of <see cref="PausedSsrc"/>, 0 for none.</summary>
        public uint PausedRtxSsrc { get; set; }

        public bool Carries(string mimeType) =>
            LastCodec == null || string.IsNullOrEmpty(mimeType) ||
            LastCodec.EncodingName.Equals(RtpCodec.EncodingNameOf(mimeType), StringComparison.OrdinalIgnoreCase);
    }
}
