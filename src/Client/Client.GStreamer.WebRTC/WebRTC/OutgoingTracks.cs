using System.Collections.Concurrent;
using System.Diagnostics;
using Client.Application.Ports;
using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Client.Core.Settings;
using Client.GStreamer.Audio;
using Client.GStreamer.Interop;
using Gst;
using Microsoft.Extensions.Logging;
using Shared.Models.Media;
using Task = System.Threading.Tasks.Task;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// The tracks we send: their capture branches, the sections and pads of webrtcbin they go out
/// through, and what belongs to a branch while it runs (voice gate, speech meter, the capture of a screen).
/// </summary>
internal sealed partial class OutgoingTracks
{
    private readonly TransportCore _core;
    private readonly MediaSections _sections;
    private readonly GstAudioDeviceProvider? _devices;
    private readonly IVideoMedia _video;
    private readonly Func<bool> _negotiate;
    private readonly OutgoingRetransmission _retransmission;
    private readonly Action<int, MediaSourceType, RawVideoFrame> _onVideoFrame;
    private readonly ILogger _logger;

    // The level and valve elements of the gates, see Release
    private readonly WrapperScope _heldWrappers = new();

    private readonly ConcurrentDictionary<string, OutboundTrack> _outbounds = new();
    private readonly ConcurrentDictionary<string, Bin> _pendingOutgoing = new();
    private readonly ConcurrentDictionary<string, OutgoingStream> _outgoing = new();

    // The sink pads of webrtcbin by the mline index of their sending section. A pad is requested
    // once and kept for the next track of the section: webrtcbin does not rebuild the sending
    // chain of a section whose pad was released and requested again, and nothing leaves through it.
    private readonly ConcurrentDictionary<int, Pad> _sendPads = new();

    // The voice gates of the outgoing tracks. A level message tells only which element posted it, so
    // each gate is found by the native handle of its level elements, the one of the gate and the one
    // of the voice detection: the handle stays the same, while the binding may hand out a new
    // wrapper object for the element every time.
    private readonly ConcurrentDictionary<IntPtr, VoiceGate> _gates = new();

    // The captures of the outgoing video tracks, keyed by the outbound id
    private readonly ConcurrentDictionary<string, OutgoingVideo> _outgoingVideo = new();

    // What feeds the capture element of an outgoing audio track from a thread of its own, keyed by the outbound id
    private readonly ConcurrentDictionary<string, IDisposable> _audioResources = new();

    // Where the RTP sequence of a stopped track goes on when it is started again, keyed by the outbound id
    private readonly ConcurrentDictionary<string, int> _sequenceNumbers = new();

    // More packets than a branch sends between being told to stop and having stopped
    private const int SequenceNumberMargin = 128;

    // One capture part is replaced at a time, and not while its branch is being released
    private readonly Lock _captureLock = new();

    // Speech of our own outbound tracks, keyed by the outbound id
    private readonly ConcurrentDictionary<string, OutgoingMeter> _outgoingMeters = new();

    // Three frames of silence reach the encoder before the valve closes
    private static readonly TimeSpan MuteValveDelay = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan UnmuteFadeDelay = TimeSpan.FromMilliseconds(20);
    private readonly ConcurrentDictionary<string, int> _muteGenerations = new();

    /// <param name="devices">Needed only to send a <see cref="DeviceAudioInput"/>.</param>
    /// <param name="video">Captures and encodes the screens that are sent.</param>
    /// <param name="negotiate">Runs a negotiation round after the set of sections changed.</param>
    /// <param name="retransmission">Resends the lost packets of the tracks that have an RTX stream.</param>
    /// <param name="onVideoFrame">Receives the preview frames of the outgoing video tracks.</param>
    public OutgoingTracks(TransportCore core, MediaSections sections, GstAudioDeviceProvider? devices,
        IVideoMedia video, Func<bool> negotiate, OutgoingRetransmission retransmission,
        Action<int, MediaSourceType, RawVideoFrame> onVideoFrame, ILogger logger)
    {
        _core = core;
        _sections = sections;
        _devices = devices;
        _video = video;
        _negotiate = negotiate;
        _retransmission = retransmission;
        _onVideoFrame = onVideoFrame;
        _logger = logger;
    }

    public OutboundTrack? Find(string outboundId) => _outbounds.GetValueOrDefault(outboundId);

    public OutboundTrack? FindBySsrc(uint ssrc) => _outbounds.Values.FirstOrDefault(outbound => outbound.SSRC == ssrc);

    /// <summary>
    /// What the last queue of every running track holds: the time in ms and the packets.
    /// </summary>
    public List<(OutboundTrack Track, double LevelMs, uint Packets)> ReadSendQueues()
    {
        var levels = new List<(OutboundTrack, double, uint)>();

        foreach (var (outboundId, outgoing) in _outgoing)
        {
            if (Find(outboundId) is not { } track)
                continue;

            try
            {
                using var queue = outgoing.Branch.GetByName(PipelineBranch.SendQueueName);

                if (queue == null)
                    continue;

                var packets = queue.GetProperty<uint>("current-level-buffers");
                var levelMs = packets == 0 ? 0 : queue.GetProperty<ulong>("current-level-time") / 1_000_000.0;

                levels.Add((track, levelMs, packets));
            }
            catch (ObjectDisposedException)
            {
                // The track stopped meanwhile
            }
        }

        return levels;
    }

    /// <summary>
    /// Adds the users whose outgoing tracks carry speech at the moment.
    /// </summary>
    public void CollectSpeaking(long now, HashSet<int> speaking)
    {
        foreach (var outgoing in _outgoingMeters.Values)
        {
            if (outgoing.Meter.IsSpeaking(now))
                speaking.Add(outgoing.UserId);
        }
    }

    /// <summary>
    /// The first step of a teardown, before the pipeline stops: the captures push into elements of the
    /// pipeline, and the capture threads of the sound work on them, so they stop before it does.
    /// </summary>
    public void StopCapture()
    {
        var outgoingVideo = _outgoingVideo.Values.ToArray();
        _outgoingVideo.Clear();

        foreach (var video in outgoingVideo)
            video.Dispose();

        foreach (var outboundId in _audioResources.Keys)
        {
            if (_audioResources.TryRemove(outboundId, out var resource))
                resource.Dispose();
        }

        foreach (var pending in _pendingOutgoing.Values)
            pending.Dispose();

        _pendingOutgoing.Clear();
        _gates.Clear();
    }

    /// <summary>
    /// The last step of a teardown, once the pipeline has stopped: the wrappers of pads with a
    /// probe have to outlive the streaming threads.
    /// </summary>
    public void Release(Element? webrtc)
    {
        foreach (var meter in _outgoingMeters.Values)
            meter.Pad.Dispose();

        _outgoingMeters.Clear();

        foreach (var outgoing in _outgoing.Values)
            outgoing.Branch.Dispose();

        _outgoing.Clear();

        foreach (var pad in _sendPads.Values)
        {
            webrtc?.ReleaseRequestPad(pad);
            pad.Dispose();
        }

        _sendPads.Clear();

        _heldWrappers.Release();
    }

    /// <summary>
    /// Starts sending an audio track from any input: a capture device, a looped file or a tone. A
    /// device chosen in the settings may be unplugged by now, so the default one is used instead
    /// of sending silence.
    /// </summary>
    public bool Start(OutboundTrack outbound, AudioInput input, double volume, VoiceGateOptions? voiceGate,
        NoiseSuppressionLevel noiseSuppression, bool isEchoCancellationEnabled, bool isAutoGainControlEnabled)
    {
        if (_outbounds.ContainsKey(outbound.Id))
            return true;

        var source = CreateSource(input);

        if (source == null)
        {
            LogAudioSourceCreateFailed(_core.SessionId, input);
            return false;
        }

        // Only a microphone is processed and gated: a file or a tone is meant to be heard as it is
        if (!Start(outbound, source.Element, volume, input is DeviceAudioInput, voiceGate, noiseSuppression,
                isEchoCancellationEnabled, isAutoGainControlEnabled))
        {
            source.Resource?.Dispose();
            return false;
        }

        if (source.Resource != null)
            _audioResources[outbound.Id] = source.Resource;

        return true;
    }

    private AudioSource? CreateSource(AudioInput input)
    {
        switch (input)
        {
            case DeviceAudioInput device when _devices != null:
                return AudioSource.Of(_devices.CreateInputElement(device.DeviceId) ?? _devices.CreateInputElement(null));
            case DeviceAudioInput:
                return null;
            case ProcessLoopbackAudioInput loopback:
                return AudioSource.Of(ProcessLoopbackSource.Create(loopback));
            case PipeWireAppAudioInput app:
                var pipeWire = PipeWireAppAudioSource.Create(app,
                    exception => LogAudioAppStreamsFailed(exception, _core.SessionId));
                return pipeWire == null ? null : new AudioSource(pipeWire.Element, pipeWire);
            default:
                return AudioSource.Of(FileAudioSource.CreateElement(input));
        }
    }

    /// <summary>
    /// Starts sending a track the SFU created a producer for. The section is added on the next
    /// negotiation round, which also links <paramref name="source"/> to webrtcbin.
    /// </summary>
    /// <param name="source">Capture element; the transport takes ownership of it.</param>
    /// <param name="volume">Gain applied before encoding.</param>
    /// <param name="isMicrophone">
    /// Whether the track gets the microphone processing and a voice gate. The gate can be turned on
    /// and off later, but not added.
    /// </param>
    /// <param name="voiceGate">
    /// How the gate of a gated track decides, or null to send it all the time.
    /// </param>
    /// <param name="noiseSuppression">How much background noise is taken out of a microphone.</param>
    /// <param name="isEchoCancellationEnabled">Whether the played voices are taken out of a microphone.</param>
    /// <param name="isAutoGainControlEnabled">Whether a quiet microphone is brought up to the level of speech.</param>
    public bool Start(OutboundTrack outbound, Element source, double volume, bool isMicrophone,
        VoiceGateOptions? voiceGate, NoiseSuppressionLevel noiseSuppression, bool isEchoCancellationEnabled,
        bool isAutoGainControlEnabled)
    {
        var branch = OutgoingAudioBranch.Build(outbound, source, volume, isMicrophone,
            isMicrophone ? Resolve(noiseSuppression) : noiseSuppression,
            isMicrophone && isEchoCancellationEnabled ? _core.Player?.EnsureEchoProbe() : null,
            isAutoGainControlEnabled, _sequenceNumbers.GetValueOrDefault(outbound.Id, -1));

        if (branch == null)
        {
            LogCaptureBranchBuildFailed(_core.SessionId);
            return false;
        }

        if (!_outbounds.TryAdd(outbound.Id, outbound))
        {
            branch.Dispose();
            return true;
        }

        if (branch.GetByName(MicrophoneChain.GateLevelName) is { } level &&
            branch.GetByName(MicrophoneChain.GateValveName) is { } valve)
        {
            var detector = branch.GetByName(MicrophoneChain.VoiceLevelName) is { } voiceLevel &&
                           branch.GetByName(MicrophoneChain.VoiceValveName) is { } voiceValve
                ? new VoiceGate.VoiceDetector(_heldWrappers.Hold(voiceLevel), _heldWrappers.Hold(voiceValve))
                : null;

            var gate = new VoiceGate(outbound.Id, _heldWrappers.Hold(level), _heldWrappers.Hold(valve), detector,
                voiceGate);

            WarnIfNoVoiceDetection(gate, voiceGate);

            // Both level elements post to the same gate
            _gates[level.Handle] = gate;

            if (detector != null)
                _gates[detector.Level.Handle] = gate;
        }

        // Watches what really leaves: nothing while muted or gated. The sound of a screen is not
        // speech, and would show us speaking for as long as a film plays.
        if (outbound.SourceType != MediaSourceType.Screen)
        {
            var meter = new SpeechActivityMeter();

            // The wrapper stays with the meter: the probe is called with a wrapper of the pad, so
            // this one has to outlive the streaming threads
            var output = branch.GetStaticPad("src")!;
            meter.Attach(output);
            _outgoingMeters[outbound.Id] = new OutgoingMeter(outbound.UserId, meter, output);
        }

        return RegisterOutgoing(outbound, branch);
    }

    /// <summary>
    /// Starts sending a video track captured from <paramref name="input"/> to a producer the SFU
    /// created for it. The frames that are sent also come back through the frame handler given to the constructor.
    /// </summary>
    /// <param name="settings">The size and the frame rate the picture is sent with.</param>
    public bool Start(OutboundTrack outbound, VideoInput input, ScreenShareSettings settings)
    {
        if (_outbounds.ContainsKey(outbound.Id))
            return true;

        var codec = OutgoingVideoBranch.CodecOf(outbound);
        var capture = _video.CreateCapture(input, settings, codec, _core.SessionId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (capture == null)
        {
            LogVideoSourceCreateFailed(_core.SessionId, input);
            return false;
        }

        var branch = OutgoingVideoBranch.Build(outbound, codec, settings.Framerate,
            _sequenceNumbers.GetValueOrDefault(outbound.Id, -1));

        if (branch?.GetByName(OutgoingVideoBranch.SourceName) is not { } source)
        {
            branch?.Dispose();
            capture.Dispose();
            LogCaptureBranchBuildFailed(_core.SessionId);
            return false;
        }

        if (!_outbounds.TryAdd(outbound.Id, outbound))
        {
            source.Dispose();
            branch.Dispose();
            capture.Dispose();
            return true;
        }

        // The keyframes the SFU asks for reach the appsrc from webrtcbin. When the first request of a
        // wait came, logged with the keyframe that answers it: an encoder that ignores the requests
        // leaves a new viewer without a picture until it makes a keyframe by itself
        long keyFrameRequested = 0;

        using (var output = source.GetStaticPad("src"))
        {
            if (output != null)
                GstEncodedNative.WatchKeyFrameRequests(output, () =>
                {
                    // Only the first request of a wait is timed: the SFU repeats it until the keyframe
                    // comes. The capture spaces the keyframes out, which the time includes
                    if (Interlocked.CompareExchange(ref keyFrameRequested, Stopwatch.GetTimestamp(), 0) == 0)
                        LogKeyFrameRequested(_core.SessionId, outbound.SSRC);

                    capture.RequestKeyFrame();
                });
        }

        // Stamped again by the appsrc: the RTP timestamps follow the clock of this pipeline. The
        // appsrc stays with the capture, which pushes into it until it is disposed. When the packets
        // back up, the frames up to a keyframe are dropped and the encoder asked for one at once
        var maxFrames = OutgoingVideoBranch.MaxQueuedFrames(settings.Framerate);
        var gate = new EncodedFrameGate(source, maxFrames);
        capture.EncodedFrameReady += frame =>
        {
            if (frame.IsKeyFrame && Interlocked.Exchange(ref keyFrameRequested, 0) is var requested and > 0)
                LogKeyFrameEncoded(_core.SessionId, outbound.SSRC, (long)Stopwatch.GetElapsedTime(requested).TotalMilliseconds);

            if (!gate.Push(frame with { Timestamp = -1 }))
                return;

            // The keyframe may have been the one dropped, which the capture would wait after
            LogVideoFramesDropped(_core.SessionId, outbound.SSRC, gate.Dropped);
            capture.RequestKeyFrame(isUrgent: true);
        };
        capture.PreviewFrameReady += frame => _onVideoFrame(outbound.UserId, outbound.SourceType, frame);

        if (!capture.Start())
        {
            _outbounds.TryRemove(outbound.Id, out _);
            capture.Dispose();
            source.Dispose();
            branch.Dispose();
            return false;
        }

        _outgoingVideo[outbound.Id] = new OutgoingVideo(capture, source);

        LogVideoEncoder(_core.SessionId, capture.Description);

        return RegisterOutgoing(outbound, branch);
    }

    /// <summary>
    /// Gives a capture branch a sending section and negotiates it. The branch is linked to webrtcbin
    /// during that round.
    /// </summary>
    private bool RegisterOutgoing(OutboundTrack outbound, Bin branch)
    {
        // Before the branch is linked: the RTX stream of a track is set up by its first packet
        _retransmission.Track(outbound);
        _pendingOutgoing[outbound.Id] = branch;

        using (_sections.Enter())
        {
            _sections.ClaimSending(outbound.MediaType, outbound.MimeType).OutboundId = outbound.Id;
        }

        LogOutboundRegistered(_core.SessionId, outbound.MediaType, outbound.SSRC);

        return _negotiate();
    }

    /// <summary>
    /// Stops sending a track. Its section stays as inactive, like the one of a departed inbound.
    /// </summary>
    public void Stop(string outboundId)
    {
        if (!_outbounds.TryRemove(outboundId, out _))
            return;

        _retransmission.Untrack(outboundId);

        _outgoingVideo.TryRemove(outboundId, out var video);

        // Stopped before the branch: it adds and removes elements of it from a thread of its own
        if (_audioResources.TryRemove(outboundId, out var audioResource))
            audioResource.Dispose();

        // Only an estimate: the branch keeps sending until it is shut down, off this thread. A
        // number that was already sent is refused by the SRTP replay check, which fails the first
        // packet of the next branch and stops it for good, hence the margin. The exact number
        // replaces it once the branch has stopped.
        if (FindOutgoingBranch(outboundId) is { } stoppedBranch &&
            PipelineBranch.ReadNextSequenceNumber(stoppedBranch) is { } nextSequenceNumber)
            _sequenceNumbers[outboundId] = (nextSequenceNumber + SequenceNumberMargin) & 0xFFFF;

        _muteGenerations.TryRemove(outboundId, out _);
        _outgoingMeters.TryRemove(outboundId, out var meter);

        if (_pendingOutgoing.TryRemove(outboundId, out var pending))
        {
            video?.Dispose();
            meter?.Pad.Dispose();
            pending.Dispose();
        }

        foreach (var gate in _gates.Where(item => item.Value.OutboundId == outboundId))
            _gates.TryRemove(gate.Key, out _);

        using (_sections.Enter())
        {
            var slot = _sections.Slots.FirstOrDefault(item => item.OutboundId == outboundId);

            if (slot != null)
                slot.OutboundId = null;
        }

        if (_outgoing.TryRemove(outboundId, out var outgoing))
            Task.Run(() => ReleaseOutgoing(outboundId, outgoing, video, meter));
        else if (pending == null)
        {
            // A branch that never got linked: nothing else stops its capture
            video?.Dispose();
            meter?.Pad.Dispose();
        }

        LogOutboundStopped(_core.SessionId);

        _negotiate();
    }

    /// <summary>Starts or stops the preview of a screen we send.</summary>
    public void SetPreviewShown(string outboundId, bool isShown)
    {
        if (_outgoingVideo.TryGetValue(outboundId, out var video))
            video.Capture.SetPreviewShown(isShown);
    }

    /// <summary>
    /// Stops or resumes sending an outbound track without touching its section or its producer.
    /// The sound goes silent at once; the valve follows a few frames later, so the encoder ends on
    /// silence. On resume the valve opens while still silent and the sound returns a frame later.
    /// </summary>
    public bool SetMuted(string outboundId, bool isMuted)
    {
        var branch = FindOutgoingBranch(outboundId);
        using var valve = branch?.GetByName(OutgoingAudioBranch.MuteValveName);

        if (branch == null || valve == null)
        {
            LogNoCaptureBranchToMute(_core.SessionId, isMuted);
            return false;
        }

        var generation = _muteGenerations.AddOrUpdate(outboundId, 1, (_, current) => current + 1);

        using (var fade = branch.GetByName(OutgoingAudioBranch.MuteFadeName))
        {
            if (fade == null)
                valve.SetProperty("mute", isMuted);
            else if (isMuted)
                fade.SetProperty("mute", true);
            else
                valve.SetProperty("mute", false);

            if (fade != null)
                _ = FinishMuteChangeAsync(outboundId, isMuted, generation);
        }

        LogOutboundMuteChanged(_core.SessionId, isMuted);

        return true;
    }

    // The second half of SetMuted, skipped when another change came in meanwhile
    private async System.Threading.Tasks.Task FinishMuteChangeAsync(string outboundId, bool isMuted, int generation)
    {
        await System.Threading.Tasks.Task.Delay(isMuted ? MuteValveDelay : UnmuteFadeDelay).ConfigureAwait(false);

        if (!_muteGenerations.TryGetValue(outboundId, out var current) || current != generation)
            return;

        var branch = FindOutgoingBranch(outboundId);

        if (isMuted)
        {
            using var valve = branch?.GetByName(OutgoingAudioBranch.MuteValveName);
            valve?.SetProperty("mute", true);
        }
        else
        {
            using var fade = branch?.GetByName(OutgoingAudioBranch.MuteFadeName);
            fade?.SetProperty("mute", false);
        }
    }

    public void SetVolume(string outboundId, double volume)
    {
        using var element = FindOutgoingBranch(outboundId)?.GetByName(MicrophoneChain.InputVolumeName);

        // Every save of the audio settings passes the volume, whatever was changed
        if (element == null || element.GetProperty<double>("volume") == volume)
            return;

        element.SetProperty("volume", volume);
        LogOutboundVolumeChanged(_core.SessionId, volume);
    }

    public void SetVoiceGate(string outboundId, VoiceGateOptions? voiceGate)
    {
        // A gate with voice detection is held under both of its level elements
        foreach (var gate in _gates.Values.Distinct().Where(gate => gate.OutboundId == outboundId))
        {
            if (!gate.SetOptions(voiceGate))
                continue;

            LogVoiceGateOptionsChanged(_core.SessionId, voiceGate);
            WarnIfNoVoiceDetection(gate, voiceGate);
        }
    }

    /// <summary>
    /// A gate asked to open only for a voice in a runtime without RNNoise opens for any loud sound,
    /// keys included, which is reported, since the user chose otherwise.
    /// </summary>
    private void WarnIfNoVoiceDetection(VoiceGate gate, VoiceGateOptions? voiceGate)
    {
        if (voiceGate is { IsVoiceDetectionEnabled: true } && !gate.HasVoiceDetection)
            LogVoiceDetectionUnavailable(_core.SessionId);
    }

    /// <summary>
    /// Makes a running audio track capture from another input, or with another processing of a
    /// microphone, while its section, its producer and its RTP stream stay. Done off the calling thread: stopping
    /// a capture device may take a while.
    /// </summary>
    public void SetInput(string outboundId, AudioInput input, NoiseSuppressionLevel noiseSuppression,
        bool isEchoCancellationEnabled, bool isAutoGainControlEnabled)
    {
        Task.Run(() =>
        {
            try
            {
                lock (_captureLock)
                {
                    // Stopped meanwhile, then its branch is gone or about to be
                    if (!_outbounds.TryGetValue(outboundId, out var outbound) ||
                        FindOutgoingBranch(outboundId) is not { } branch)
                        return;

                    var isScreen = outbound.SourceType == MediaSourceType.Screen;

                    var source = CreateSource(input);

                    if (source == null)
                    {
                        LogAudioSourceCreateFailed(_core.SessionId, input);
                        return;
                    }

                    // The previous source stops feeding its element before the element is replaced
                    if (_audioResources.TryRemove(outboundId, out var previous))
                        previous.Dispose();

                    var isMicrophone = input is DeviceAudioInput && !isScreen;

                    if (isMicrophone)
                        noiseSuppression = Resolve(noiseSuppression);

                    if (MicrophoneChain.ReplaceCapture(branch, source.Element, isMicrophone, isScreen,
                            noiseSuppression, isMicrophone && isEchoCancellationEnabled ? _core.Player?.EnsureEchoProbe() : null,
                            isAutoGainControlEnabled))
                    {
                        if (source.Resource != null)
                            _audioResources[outboundId] = source.Resource;

                        LogOutboundInputChanged(_core.SessionId, input, noiseSuppression);
                    }
                    else
                    {
                        source.Resource?.Dispose();
                        LogCaptureBranchBuildFailed(_core.SessionId);
                    }
                }
            }
            catch (Exception exception)
            {
                LogOutboundInputChangeFailed(exception, _core.SessionId);
            }
        });
    }

    /// <summary>
    /// The noise suppression a microphone really gets: a runtime without RNNoise runs the neural
    /// levels as the moderate one, which is reported, since the user chose otherwise.
    /// </summary>
    private NoiseSuppressionLevel Resolve(NoiseSuppressionLevel requested)
    {
        var applied = MicrophoneProcessing.Resolve(requested);

        if (applied != requested)
            LogNoiseSuppressionReplaced(_core.SessionId, requested, applied);

        return applied;
    }

    /// <summary>
    /// The capture branch of an outbound track, linked to webrtcbin or still waiting for its section.
    /// </summary>
    private Bin? FindOutgoingBranch(string outboundId) =>
        _outgoing.TryGetValue(outboundId, out var outgoing)
            ? outgoing.Branch
            : _pendingOutgoing.GetValueOrDefault(outboundId);

    /// <summary>
    /// Links the capture branches of new outbound sections to webrtcbin. Runs between applying the
    /// remote description, which creates the transceivers, and creating the answer, which then
    /// sends on them.
    /// </summary>
    public void AttachPending()
    {
        List<(int Index, string OutboundId)> pending;

        using (_sections.Enter())
        {
            pending = _sections.Slots
                .Select((slot, index) => (Slot: slot, Index: index))
                .Where(item => item.Slot.OutboundId != null && _pendingOutgoing.ContainsKey(item.Slot.OutboundId))
                .Select(item => (item.Index, item.Slot.OutboundId!))
                .ToList();
        }

        foreach (var (index, outboundId) in pending)
        {
            if (!_pendingOutgoing.TryRemove(outboundId, out var branch))
                continue;

            var webrtc = _core.WebRTC;
            var pipeline = _core.Pipeline;

            if (webrtc == null || pipeline == null)
            {
                branch.Dispose();
                return;
            }

            using (var transceiver = GstNative.EmitGetObject<Gst.GObject.Object>(webrtc, "get-transceiver", index))
            {
                if (transceiver != null)
                    Global.UtilSetObjectArg(transceiver, "direction", "sendonly");
            }

            // Naming the pad after the mline binds it to the transceiver of that section.
            if (!_sendPads.TryGetValue(index, out var sinkPad))
            {
                sinkPad = webrtc.RequestPadSimple($"sink_{index}");

                if (sinkPad == null)
                {
                    LogSendPadRequestFailed(_core.SessionId, index);
                    branch.Dispose();
                    continue;
                }

                _sendPads[index] = sinkPad;
            }

            // The track that used the section a moment ago is released off this thread and may
            // not have let go of the pad yet
            if (sinkPad.GetPeer() is { } previous)
            {
                using (previous)
                    previous.Unlink(sinkPad);
            }

            pipeline.Add(branch);

            PadLinkReturn result;

            using (var output = branch.GetStaticPad("src"))
                result = output?.Link(sinkPad) ?? PadLinkReturn.Refused;

            if (result != PadLinkReturn.Ok)
            {
                LogCaptureBranchLinkFailed(_core.SessionId, index, result);

                // The capture would go on encoding into a branch that is gone, and Stop finds the
                // branch neither pending nor sending, so it would never be stopped
                if (_outgoingVideo.TryRemove(outboundId, out var unlinked))
                    unlinked.Dispose();

                pipeline.Remove(branch);
                branch.Dispose();
                continue;
            }

            branch.SyncStateWithParent();
            _outgoing[outboundId] = new OutgoingStream(branch, sinkPad);

            if (_outgoingVideo.TryGetValue(outboundId, out var video))
            {
                // What was encoded before the branch played went nowhere, the first keyframe with it
                video.Capture.RequestKeyFrame(isUrgent: true);
                LogSendingVideo(_core.SessionId, index);
            }
            else
                LogSendingAudio(_core.SessionId, index);
        }
    }

    private void ReleaseOutgoing(string outboundId, OutgoingStream outgoing, OutgoingVideo? video,
        OutgoingMeter? meter)
    {
        try
        {
            // Stopped first: it must not push into elements that are shutting down
            video?.Dispose();

            lock (_captureLock)
                GstBranch.Shutdown(outgoing.Branch);

            meter?.Pad.Dispose();

            // Nothing leaves the branch anymore, so this is the number the next one continues with,
            // unless the track was started again meanwhile and took the estimate
            if (!_outbounds.ContainsKey(outboundId) &&
                PipelineBranch.ReadNextSequenceNumber(outgoing.Branch) is { } nextSequenceNumber)
                _sequenceNumbers[outboundId] = nextSequenceNumber;

            // Removing the branch unlinks it; the pad of webrtcbin stays for the next track
            _core.Pipeline?.Remove(outgoing.Branch);
            outgoing.Branch.Dispose();
        }
        catch (Exception exception)
        {
            LogCaptureBranchReleaseFailed(exception, _core.SessionId);
        }
    }

    private sealed record OutgoingStream(Bin Branch, Pad SinkPad);

    /// <param name="Resource">What feeds the element from a thread of its own, or null.</param>
    private sealed record AudioSource(Element Element, IDisposable? Resource = null)
    {
        public static AudioSource? Of(Element? element) => element == null ? null : new AudioSource(element);
    }

    /// <param name="Source">The appsrc of the branch the capture pushes into.</param>
    private sealed record OutgoingVideo(IScreenCapture Capture, Element Source) : IDisposable
    {
        // The capture first: it pushes into the appsrc until it stops
        public void Dispose()
        {
            Capture.Dispose();
            Source.Dispose();
        }
    }

    /// <param name="Pad">The pad the meter watches, disposed once its branch has stopped.</param>
    private sealed record OutgoingMeter(int UserId, SpeechActivityMeter Meter, Pad Pad);

    /// <summary>
    /// Logs what every running voice gate did since the previous call: how much of the time it let
    /// the voice through, how loud the voice was against its threshold, with the adaptive threshold
    /// how loud the room was and where that put the threshold, and with voice detection how much
    /// of the time RNNoise heard a voice. A voice that grows
    /// quieter over a session, as the automatic gain of the processing may make it, shows here as
    /// a gate that opens less and less. Called by a timer of the transport.
    /// </summary>
    public void ReportGates()
    {
        foreach (var gate in _gates.Values.Distinct())
        {
            if (gate.TakeSummary() is { } summary)
                LogVoiceGateSummary(_core.SessionId, summary.ThresholdDb, summary.EffectiveThresholdDb,
                    summary.NoiseFloorDb, summary.OpenPercent, summary.Openings, summary.LoudestDb,
                    summary.AverageOpenPeakDb, summary.VoicePercent);
        }
    }

    /// <summary>
    /// Passes the level messages of the capture branches to their gates. Runs on the bus watcher.
    /// </summary>
    public void OnElementMessage(Message message)
    {
        // Read without a wrapper: one made here for an element nobody else wraps, such as the RTP
        // manager inside webrtcbin, would never be disposed and would keep that element for good
        var source = GstNative.GetMessageSource(message);

        if (!_gates.TryGetValue(source, out var gate) || !gate.Update(source, message))
            return;

        LogVoiceGateChanged(_core.SessionId, gate.IsOpen);
    }
}
