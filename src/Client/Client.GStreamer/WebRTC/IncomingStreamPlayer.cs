using System.Collections.Concurrent;
using Client.Core.Media;
using Gst;
using Microsoft.Extensions.Logging;
using Client.GStreamer.Audio;
using Client.GStreamer.Interop;
using Client.GStreamer.Video;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// Gives every src pad webrtcbin adds a branch of its own. Opus audio is decoded and played on the
/// default output device, H.264 video is decoded into raw frames for the caller to draw; any other
/// stream is drained into a fakesink, because a pad left unlinked would stop the whole transport
/// with not-linked.
/// </summary>
/// <remarks>
/// Branches follow the pads, not the inbound tracks. webrtcbin adds a pad for every new SSRC and
/// never removes one, so the next track of a section arrives on a pad of its own while the pad of
/// the previous track stays; the transport retires the branch of a track that is over. The volume
/// is set per section, and the transport sets it again whenever a section changes hands.
/// </remarks>
internal sealed partial class IncomingStreamPlayer : IDisposable
{
    // A lost packet is rebuilt from the redundancy in the next one, which the decoder waits one
    // frame for, and concealed when that one is lost too. The branch ends in a pad of the mixer.
    // The queue drops the oldest packets past 200 ms: with the default second of room a stall
    // would fill it, and a live stream never drains it again, so the delay would only grow.
    private const string OpusPlaybackDescription =
        "queue max-size-buffers=0 max-size-bytes=0 max-size-time=200000000 leaky=downstream ! rtpopusdepay ! opusdec plc=true use-inband-fec=true ! audioconvert ! audioresample ! " +
        "capsfilter caps=\"audio/x-raw,format=F32LE\" ! volume name=volume";

    // One output for all voices, running on the clock whether packets come or not: DTX sends nothing
    // in a pause, and a sink fed by the packets alone runs dry, resyncs and replays a piece of the
    // voice. The mixer fills what is missing with silence on time. It starts at the first input
    // rather than at running time zero, which would make it catch up the whole session first. It
    // waits 20 ms, one Opus frame, for a late buffer: without that wait a voice arriving a little
    // unevenly is cut into pieces of silence, which crackles, and every millisecond more is heard
    // as delay. The voices are mixed in float, so a voice raised past full scale is not cut off
    // yet; the soft knee then bends the peaks under it instead of the hard clipping of integer
    // samples, which a loud voice in overdrive crackles with.
    private const string OutputDescription =
        "audiomixer name=" + MixerName + " start-time-selection=first latency=20000000 ! " +
        "capsfilter caps=\"audio/x-raw,format=F32LE,rate=48000,channels=2\" ! " +
        "audiodynamic mode=compressor characteristics=soft-knee threshold=0.7 ratio=0 ! " +
        "audioconvert ! audioresample ! ";

    private const string MixerName = "voices";

    // Decodes like playback, so a broken stream still shows up, but plays nothing
    private const string OpusSilentDescription =
        "queue ! rtpopusdepay ! opusdec plc=true use-inband-fec=true ! fakesink sync=false async=false";

    // No queue: it would cost a thread for every stream that is drained
    private const string DrainDescription = "fakesink sync=false async=false";

    private const string VideoSinkName = "frames";

    // A lost packet breaks every frame up to the next keyframe, so the depayloader asks the sender
    // for one and holds the broken frames back meanwhile
    private const string H264Description =
        "queue ! rtph264depay request-keyframe=true ! " +
        "h264parse ! {0} ! {1}";

    private const string VolumeElementName = "volume";

    private readonly Pipeline _pipeline;
    private readonly ILogger _logger;
    private readonly int _sessionId;
    private readonly ConcurrentDictionary<string, Branch> _branches = new();
    private volatile bool _isDisposed;

    private readonly Lock _outputLock = new();
    private Bin? _output;
    private Element? _mixer;

    private readonly bool _isPlaybackEnabled;
    private readonly Func<uint, double> _resolveVolume;
    private readonly Action<uint, RawVideoFrame> _onVideoFrame;

    /// <param name="isPlaybackEnabled">
    /// Whether incoming audio is played on the output device. Headless clients such as the test bot
    /// still receive and decode it, but keep it off the speakers.
    /// </param>
    /// <param name="logger">The transport's logger; messages carry <paramref name="sessionId"/> like its own.</param>
    /// <param name="resolveVolume">
    /// Playback volume of the section with the given mline index, applied to a branch as it is built.
    /// </param>
    /// <param name="onVideoFrame">
    /// Receives the decoded frames of the section with the given mline index, on a thread of the
    /// branch. The pixels are only valid during the call.
    /// </param>
    public IncomingStreamPlayer(Pipeline pipeline, ILogger logger, int sessionId, Func<uint, double> resolveVolume,
        Action<uint, RawVideoFrame> onVideoFrame, bool isPlaybackEnabled = true)
    {
        _onVideoFrame = onVideoFrame;
        _pipeline = pipeline;
        _logger = logger;
        _sessionId = sessionId;
        _resolveVolume = resolveVolume;
        _isPlaybackEnabled = isPlaybackEnabled;

        VideoSurfaceSupport.Failed += OnSurfaceFailed;
    }

    /// <summary>
    /// Links a new src pad of webrtcbin. Called from a streaming thread.
    /// </summary>
    public void Attach(Pad pad, Caps caps)
    {
        var padName = pad.Name ?? string.Empty;

        if (_isDisposed || _branches.ContainsKey(padName))
            return;

        var isOpus = IsOpus(caps);
        var mline = GetMline(pad);

        // Frames of a section that cannot be resolved would belong to nobody
        var video = !isOpus && mline != null && IsH264(caps) ? BuildVideoBranch(padName, mline.Value, true) : null;

        var description = !isOpus ? DrainDescription
            : _isPlaybackEnabled ? OpusPlaybackDescription
            : OpusSilentDescription;
        var branch = video?.Bin ?? Global.ParseBinFromDescription(description, true);

        if (branch == null)
        {
            LogBranchBuildFailed(_sessionId, padName);
            return;
        }

        branch.SetName($"incoming_{padName}");

        // Set before the branch plays, so a muted speaker is not heard for a moment
        if (mline != null)
        {
            using var volume = branch.GetByName(VolumeElementName);
            volume?.SetProperty("volume", _resolveVolume(mline.Value));
        }

        _pipeline.Add(branch);

        var isPlayed = isOpus && _isPlaybackEnabled;
        var mixerPad = isPlayed ? LinkToOutput(branch) : null;

        PadLinkReturn result;

        using (var input = branch.GetStaticPad("sink"))
            result = isPlayed && mixerPad == null ? PadLinkReturn.Refused
                : input == null ? PadLinkReturn.Refused
                : pad.Link(input);

        if (result != PadLinkReturn.Ok)
        {
            LogBranchLinkFailed(_sessionId, padName, result);
            video?.Pump.Dispose();
            UnlinkFromOutput(branch, mixerPad);
            _pipeline.Remove(branch);
            branch.Dispose();
            return;
        }

        branch.SyncStateWithParent();

        SpeechActivityMeter? meter = null;

        if (isOpus)
        {
            meter = new SpeechActivityMeter();
            meter.Attach(pad);
        }

        _branches[padName] = new Branch(branch, mline, meter, video?.Pump, pad)
        {
            MixerPad = mixerPad,
            Output = video?.Output.Kind,
            Chain = video?.Output.Chain
        };

        if (video != null)
            LogDecodingVideo(_sessionId, padName, video.Value.Output.Chain, video.Value.Output.Kind);
        else if (!isOpus)
            LogDraining(_sessionId, padName);
        else if (_isPlaybackEnabled)
            LogPlaying(_sessionId, padName);
        else
            LogReceivingSilently(_sessionId, padName);
    }

    /// <summary>
    /// Drops the branch of a removed pad. The teardown runs off the calling thread, since changing
    /// the state of an element from a streaming thread of the same pipeline can deadlock.
    /// </summary>
    public void Detach(Pad pad)
    {
        if (!_branches.TryRemove(pad.Name ?? string.Empty, out var branch))
            return;

        System.Threading.Tasks.Task.Run(() => Release(branch));
    }

    /// <summary>
    /// Stops decoding the stream of the section with the given mline index, once its track is over.
    /// The next track of the section arrives on a pad of its own, so the decoder of this one, and
    /// for audio its output stream, would stay behind for good; its pad is kept linked to a drain
    /// instead.
    /// </summary>
    public void Retire(uint mline)
    {
        foreach (var (padName, branch) in _branches)
        {
            // What has neither is a drain already
            if (branch.Mline != mline || (branch.Pump == null && branch.Meter == null) ||
                !_branches.TryRemove(padName, out _))
                continue;

            System.Threading.Tasks.Task.Run(() => ReplaceWithDrain(padName, branch));
        }
    }

    private void ReplaceWithDrain(string padName, Branch branch)
    {
        try
        {
            // Stopped first: the pump must not pull from a sink that is shutting down
            branch.Pump?.Dispose();

            if (_isDisposed)
                return;

            branch.Meter?.Detach(branch.Pad);

            using (var input = branch.Bin.GetStaticPad("sink"))
            {
                if (input != null)
                    branch.Pad.Unlink(input);
            }

            PipelineBranch.Shutdown(branch.Bin);
            UnlinkFromOutput(branch.Bin, branch.MixerPad);
            _pipeline.Remove(branch.Bin);
            branch.Bin.Dispose();

            var drain = Global.ParseBinFromDescription(DrainDescription, true);

            if (drain == null || _isDisposed)
                return;

            drain.SetName($"incoming_{padName}_retired");
            _pipeline.Add(drain);

            PadLinkReturn linked;

            using (var input = drain.GetStaticPad("sink"))
                linked = input == null ? PadLinkReturn.Refused : branch.Pad.Link(input);

            if (linked != PadLinkReturn.Ok)
            {
                _pipeline.Remove(drain);
                drain.Dispose();
                return;
            }

            drain.SyncStateWithParent();
            _branches[padName] = new Branch(drain, branch.Mline, null, null, branch.Pad);

            LogRetired(_sessionId, padName);
        }
        catch (Exception exception)
        {
            LogReleaseFailed(exception, _sessionId, $"incoming_{padName}");
        }
    }

    /// <summary>
    /// A branch that decodes H.264 into frames.
    /// </summary>
    /// <param name="allowGpuSurfaces">False to deliver the frames in memory whatever the screen supports.</param>
    /// <returns>Null when the runtime has no decoder or an element is missing.</returns>
    private (Bin Bin, VideoFramePump Pump, VideoOutput Output)? BuildVideoBranch(string padName, uint mline,
        bool allowGpuSurfaces)
    {
        var output = VideoOutputSelector.Select(VideoCodec.H264, allowGpuSurfaces);

        if (output == null)
            return null;

        var branch = Global.ParseBinFromDescription(
            string.Format(H264Description, output.Chain, VideoFramePump.SinkDescription(VideoSinkName)), true);

        // An element of the chosen chain is missing after all: the plain chain needs none of them
        if (branch == null && output.Kind != VideoOutputKind.Memory)
        {
            output = VideoOutputSelector.Plain(output.Decoder);
            branch = Global.ParseBinFromDescription(
                string.Format(H264Description, output.Chain, VideoFramePump.SinkDescription(VideoSinkName)), true);
        }

        if (branch?.GetByName(VideoSinkName) is not { } sink)
        {
            branch?.Dispose();
            return null;
        }

        var pump = new VideoFramePump(sink, output.Kind, $"video_{_sessionId}_{padName}",
            frame => _onVideoFrame(mline, frame),
            exception => LogVideoFrameFailed(exception, _sessionId, padName),
            kind => OnBranchSurfaceFailed(padName, kind));

        return (branch, pump, output);
    }

    // A pump found its frames cannot be delivered as they come; on its own thread
    private void OnBranchSurfaceFailed(string padName, VideoOutputKind kind)
    {
        VideoSurfaceSupport.ReportFailure(ToSurfaceKind(kind));

        // Reported already when the kind was taken out of the supported ones, but a kind the
        // screen never claimed to support is rebuilt here
        if (_branches.TryGetValue(padName, out var branch) && branch.Output == kind)
            ScheduleRebuild(padName, branch);
    }

    // The screen found a kind does not work; on whatever thread it found out
    private void OnSurfaceFailed(VideoSurfaceKinds kinds)
    {
        foreach (var (padName, branch) in _branches)
        {
            if (branch.Output is { } output && output != VideoOutputKind.Memory && (ToSurfaceKind(output) & kinds) != 0)
                ScheduleRebuild(padName, branch);
        }
    }

    private static VideoSurfaceKinds ToSurfaceKind(VideoOutputKind kind) => kind switch
    {
        VideoOutputKind.DmaBuf => VideoSurfaceKinds.DmaBuf,
        VideoOutputKind.Cuda => VideoSurfaceKinds.CudaGl,
        VideoOutputKind.D3D11 => VideoSurfaceKinds.D3D11Shared,
        _ => VideoSurfaceKinds.None
    };

    private void ScheduleRebuild(string padName, Branch branch)
    {
        // Only the first report of a branch rebuilds it
        if (!_branches.TryUpdate(padName, branch with { Output = VideoOutputKind.Memory, Chain = null }, branch))
            return;

        LogVideoSurfaceFailed(_sessionId, padName, branch.Output ?? VideoOutputKind.Memory,
            branch.Output == VideoOutputKind.D3D11 ? Video.D3D11SharedTextureWriter.LastFailure : null);

        // Off the thread of the pump, which disposing the pump waits for
        System.Threading.Tasks.Task.Run(() => Rebuild(padName, branch, false));
    }

    /// <summary>
    /// Replaces a video branch with one built anew, for frames in memory when
    /// <paramref name="allowGpuSurfaces"/> is false. The depayloader of the new branch waits for a
    /// keyframe and asks the sender for one.
    /// </summary>
    private void Rebuild(string padName, Branch branch, bool allowGpuSurfaces)
    {
        // The pad stays without a peer while the branch is swapped
        var dropping = GstVideoNative.DropBuffers(branch.Pad);

        try
        {
            branch.Pump?.Dispose();

            if (_isDisposed || branch.Mline is not { } mline)
                return;

            using (var input = branch.Bin.GetStaticPad("sink"))
            {
                if (input != null)
                    branch.Pad.Unlink(input);
            }

            PipelineBranch.Shutdown(branch.Bin);
            _pipeline.Remove(branch.Bin);
            branch.Bin.Dispose();

            var video = BuildVideoBranch(padName, mline, allowGpuSurfaces);

            if (video == null || _isDisposed)
            {
                video?.Pump.Dispose();
                video?.Bin.Dispose();
                _branches.TryRemove(padName, out _);
                return;
            }

            video.Value.Bin.SetName($"incoming_{padName}_{Guid.NewGuid():N}");
            _pipeline.Add(video.Value.Bin);

            PadLinkReturn linked;

            using (var input = video.Value.Bin.GetStaticPad("sink"))
                linked = input == null ? PadLinkReturn.Refused : branch.Pad.Link(input);

            if (linked != PadLinkReturn.Ok)
            {
                LogBranchLinkFailed(_sessionId, padName, linked);
                video.Value.Pump.Dispose();
                _pipeline.Remove(video.Value.Bin);
                video.Value.Bin.Dispose();
                _branches.TryRemove(padName, out _);
                return;
            }

            video.Value.Bin.SyncStateWithParent();
            _branches[padName] = new Branch(video.Value.Bin, branch.Mline, null, video.Value.Pump, branch.Pad)
            {
                Output = video.Value.Output.Kind,
                Chain = video.Value.Output.Chain
            };

            LogDecodingVideo(_sessionId, padName, video.Value.Output.Chain, video.Value.Output.Kind);
        }
        catch (Exception exception)
        {
            LogReleaseFailed(exception, _sessionId, $"incoming_{padName}");
        }
        finally
        {
            GstVideoNative.RemoveProbe(branch.Pad, dropping);
        }
    }

    /// <summary>
    /// Sets the playback volume of the section with the given mline index. A section without a
    /// branch yet gets it from <see cref="_resolveVolume"/> once its pad appears.
    /// </summary>
    /// <param name="volume">Linear gain, 0 silences the section.</param>
    public void SetVolume(uint mline, double volume)
    {
        if (_isDisposed)
            return;

        foreach (var branch in _branches.Values)
        {
            if (branch.Mline != mline)
                continue;

            using var element = branch.Bin.GetByName(VolumeElementName);

            if (element == null)
                continue;

            element.SetProperty("volume", volume);
            LogVolumeChanged(_sessionId, mline, volume);
        }
    }

    /// <summary>
    /// The mline index of the section a src pad of webrtcbin belongs to. The pad names are only
    /// counters, the section is known to the transceiver of the pad.
    /// </summary>
    private uint? GetMline(Pad pad)
    {
        try
        {
            var transceiver = GstNative.GetObject(pad.Handle, "transceiver");

            if (transceiver != IntPtr.Zero)
            {
                try
                {
                    return GstNative.GetUInt32(transceiver, "mlineindex");
                }
                finally
                {
                    GstNative.Release(transceiver);
                }
            }
        }
        catch (Exception exception)
        {
            LogMlineUnresolved(exception, _sessionId, pad.Name ?? string.Empty);
            return null;
        }

        LogMlineUnresolved(null, _sessionId, pad.Name ?? string.Empty);
        return null;
    }

    /// <summary>
    /// Links the src pad of a playback branch to a new pad of the mixer, building the output first
    /// if it is not there yet.
    /// </summary>
    /// <returns>The pad of the mixer, or null when it could not be linked.</returns>
    private Pad? LinkToOutput(Bin branch)
    {
        var mixer = EnsureOutput();

        if (mixer == null)
            return null;

        var mixerPad = mixer.RequestPadSimple("sink_%u");

        if (mixerPad == null)
            return null;

        // The mixer sits inside the output bin, and a pad only links to one of the same parent
        var result = PadLinkReturn.Refused;

        using (var ghost = GhostPad.New(mixerPad.Name, mixerPad))
        {
            lock (_outputLock)
            {
                if (ghost != null && _output != null)
                {
                    ghost.SetActive(true);

                    if (_output.AddPad(ghost))
                    {
                        using var output = branch.GetStaticPad("src");
                        result = output == null ? PadLinkReturn.Refused : output.Link(ghost);
                    }
                }
            }
        }

        if (result == PadLinkReturn.Ok)
            return mixerPad;

        UnlinkFromOutput(branch, mixerPad);
        return null;
    }

    private void UnlinkFromOutput(Bin branch, Pad? mixerPad)
    {
        if (mixerPad == null)
            return;

        lock (_outputLock)
        {
            using (var ghost = _output?.GetStaticPad(mixerPad.Name))
            {
                if (ghost != null)
                {
                    using (var output = branch.GetStaticPad("src"))
                        output?.Unlink(ghost);

                    ghost.SetActive(false);
                    _output!.RemovePad(ghost);
                }
            }

            _mixer?.ReleaseRequestPad(mixerPad);
        }

        mixerPad.Dispose();
    }

    private Element? EnsureOutput()
    {
        lock (_outputLock)
        {
            if (_mixer != null || _isDisposed)
                return _mixer;

            var output = Global.ParseBinFromDescription(OutputDescription + VoicePlaybackSink.Description, false);

            if (output?.GetByName(MixerName) is not { } mixer)
            {
                output?.Dispose();
                LogBranchBuildFailed(_sessionId, MixerName);
                return null;
            }

            output.SetName($"voices_{_sessionId}");

            // The sound server may restore a mute an earlier version left for the client name
            VoicePlaybackSink.SetMuted(output, false);

            _pipeline.Add(output);
            output.SyncStateWithParent();

            _output = output;
            _mixer = mixer;
            return mixer;
        }
    }

    private void Release(Branch branch)
    {
        try
        {
            // Stopped first: the pump must not pull from a sink that is shutting down
            branch.Pump?.Dispose();
            PipelineBranch.Shutdown(branch.Bin);

            if (!_isDisposed)
            {
                UnlinkFromOutput(branch.Bin, branch.MixerPad);
                _pipeline.Remove(branch.Bin);
            }

            branch.Bin.Dispose();
            branch.Pad.Dispose();
        }
        catch (Exception exception)
        {
            LogReleaseFailed(exception, _sessionId, branch.Bin.Name ?? string.Empty);
        }
    }


    private static bool IsOpus(Caps caps)
    {
        if (caps.GetSize() == 0)
            return false;

        using var structure = caps.GetStructure(0);

        return structure.GetString("media") == "audio" &&
               string.Equals(structure.GetString("encoding-name"), "OPUS", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsH264(Caps caps)
    {
        if (caps.GetSize() == 0)
            return false;

        using var structure = caps.GetStructure(0);

        return structure.GetString("media") == "video" &&
               string.Equals(structure.GetString("encoding-name"), "H264", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The mline indexes of the audio sections someone speaks in right now.
    /// </summary>
    /// <param name="now">A <see cref="System.Diagnostics.Stopwatch"/> timestamp.</param>
    public List<uint> GetSpeakingSections(long now) =>
        _branches.Values
            .Where(branch => branch.Mline != null && branch.Meter?.IsSpeaking(now) == true)
            .Select(branch => branch.Mline!.Value)
            .ToList();

    /// <param name="Mline">Null when the section could not be resolved; such a branch keeps its volume.</param>
    /// <param name="Meter">Null for a stream that is not audio.</param>
    /// <param name="Pump">Null for a stream that is not decoded video.</param>
    private sealed record Branch(Bin Bin, uint? Mline, SpeechActivityMeter? Meter, VideoFramePump? Pump, Pad Pad)
    {
        /// <summary>The pad of the mixer a played voice is linked to.</summary>
        public Pad? MixerPad { get; init; }

        /// <summary>The form the frames of a video branch are delivered in.</summary>
        public VideoOutputKind? Output { get; init; }

        /// <summary>The decoding chain of a video branch; null while it is being rebuilt.</summary>
        public string? Chain { get; init; }
    }

    /// <summary>
    /// Stops the pumps and takes no more pads. The pipeline must not be shut down before this
    /// returns: the pumps still pull from its sinks.
    /// </summary>
    public void Stop()
    {
        _isDisposed = true;
        VideoSurfaceSupport.Failed -= OnSurfaceFailed;

        foreach (var branch in _branches.Values)
            branch.Pump?.Dispose();
    }

    /// <summary>
    /// Forgets the branches. The elements themselves go away with the pipeline, which must be shut
    /// down by now: while packets flow, the probe of a meter is called for each of them with a
    /// wrapper of its pad, and a wrapper made for a pad whose own one is already disposed would
    /// keep the pad, its transceiver, and through it the transports and the ICE agent with all of
    /// its sockets.
    /// </summary>
    public void Dispose()
    {
        Stop();

        foreach (var branch in _branches.Values)
        {
            // Only the wrappers go, the pipeline still owns the elements. A wrapper that is merely
            // dropped would hold its object until it is collected, see WebRTCTransport.Teardown
            branch.Bin.Dispose();
            branch.Pad.Dispose();
            branch.MixerPad?.Dispose();
        }

        _branches.Clear();

        lock (_outputLock)
        {
            _mixer?.Dispose();
            _output?.Dispose();
            _mixer = null;
            _output = null;
        }
    }
}
