using Client.Application.Ports;
using Client.Core.Media;
using Client.GStreamer.Interop;
using Gst;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Video;

/// <summary>
/// Decodes one watched stream in a pipeline of its own, from an appsrc the encoded frames are pushed
/// into, through the chain <see cref="VideoOutputSelector"/> picks, to the frames of a
/// <see cref="VideoFramePump"/>.
/// </summary>
/// <remarks>
/// When GPU frames of the chosen kind fail, on the side of the pump or of the screen, the pipeline is
/// built anew for frames in memory. The new decoder needs a keyframe to start from: the frames up to
/// one are dropped and <see cref="KeyFrameNeeded"/> asks the transport for it, so the picture comes
/// back after about a second.
/// </remarks>
internal sealed partial class GstVideoDecoder : IVideoDecoder
{
    private const string SourceName = "input";
    private const string SinkName = "frames";

    // Past a few queued frames the frames up to the next keyframe are dropped and one is asked for, so
    // a decoder that fell behind, or a stalled drawing, cannot leave the picture late for good, nor
    // broken until the sender's own next keyframe. The frames keep the timestamps of their stream,
    // and nothing waits for the clock. The jitter buffer lets out at once what it held for a
    // retransmission, a few hundred ms of frames: 16 took such a bunch for a decoder that fell behind,
    // and the picture stood until the next keyframe, up to the keyframe request delay of the SFU
    private const int MaxQueuedFrames = 120;

    private const string Description =
        "appsrc name=" + SourceName + " is-live=true format=time do-timestamp=false max-buffers=120 " +
        "max-bytes=0 leaky-type=downstream caps=\"{0}\" ! {1}{2} ! {3}";

    private readonly VideoCodec _codec;
    private readonly string _name;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();

    private Running? _running;
    private bool _isDisposed;

    public event Action<RawVideoFrame>? FrameReady;
    public event Action<VideoDecoding>? DecodingChanged;
    public event Action? KeyFrameNeeded;

    private GstVideoDecoder(VideoCodec codec, string name, ILogger logger)
    {
        _codec = codec;
        _name = name;
        _logger = logger;
    }

    /// <returns>Null when the runtime has no decoder of the codec or an element is missing.</returns>
    public static GstVideoDecoder? Create(VideoCodec codec, string name, ILogger logger)
    {
        var decoder = new GstVideoDecoder(codec, name, logger);

        if (decoder.Build(true) is not { } running)
            return null;

        decoder._running = running;
        VideoSurfaceSupport.Failed += decoder.OnSurfaceFailed;
        LogDecoding(logger, name, running.Output.Chain, running.Output.Kind);

        return decoder;
    }

    public void Push(EncodedVideoFrame frame)
    {
        long dropped;

        lock (_lock)
        {
            if (_running is not { } running)
                return;

            if (!running.Gate.Push(frame))
                return;

            dropped = running.Gate.Dropped;
        }

        LogFramesDropped(_logger, _name, dropped);
        KeyFrameNeeded?.Invoke();
    }

    /// <param name="allowGpuSurfaces">False to deliver the frames in memory whatever the screen supports.</param>
    /// <param name="failedSurface">The GPU kind the previous pipeline failed with, for <see cref="DecodingChanged"/>.</param>
    /// <param name="failureReason">Why it failed, when that is known.</param>
    private Running? Build(bool allowGpuSurfaces, VideoSurfaceKinds failedSurface = VideoSurfaceKinds.None,
        string? failureReason = null)
    {
        var output = VideoOutputSelector.Select(_codec, allowGpuSurfaces);

        if (output == null)
        {
            LogNoDecoder(_logger, _name, _codec);
            return null;
        }

        var pipeline = Parse(output);

        // An element of the chosen chain is missing after all: the plain chain needs none of them
        if (pipeline == null && output.Kind != VideoOutputKind.Memory)
        {
            output = VideoOutputSelector.Plain(output.Decoder);
            pipeline = Parse(output);
        }

        if (pipeline == null)
        {
            LogBuildFailed(_logger, _name);
            return null;
        }

        var source = pipeline.GetByName(SourceName)!;
        var sink = pipeline.GetByName(SinkName)!;
        var bus = new GstBusWatcher(pipeline.GetBus(), MessageType.Error | MessageType.Warning, OnMessage,
            $"decoder_{_name}", _logger);

        // The first frame of each pipeline is logged and reported: whether the picture got through is
        // not told otherwise
        var isFirstFrame = true;
        var decoding = new VideoDecoding(output.Decoder, !VideoElementNames.IsSoftware(output.Decoder),
            ToSurfaceKind(output.Kind), failedSurface, failureReason);
        var pump = new VideoFramePump(sink, output.Kind, $"video_{_name}",
            frame =>
            {
                if (isFirstFrame)
                {
                    isFirstFrame = false;
                    LogFirstFrame(_logger, _name, frame.Width, frame.Height, output.Kind);
                    DecodingChanged?.Invoke(decoding);
                }

                FrameReady?.Invoke(frame);
            },
            exception => LogFrameFailed(_logger, exception, _name),
            OnPumpSurfaceFailed);

        var running = new Running(pipeline, source, pump, bus, output);

        if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
        {
            LogStartFailed(_logger, _name);
            running.Dispose();
            return null;
        }

        return running;
    }

    private Pipeline? Parse(VideoOutput output)
    {
        // The parser fills in the caps the decoders negotiate on, such as the profile
        var parser = VideoEncoderSelector.GetParser(_codec) is { } factory ? $"{factory} ! " : "";
        var description = string.Format(System.Globalization.CultureInfo.InvariantCulture, Description,
            VideoEncoderSelector.GetEncodedCaps(_codec), parser, output.Chain, VideoFramePump.SinkDescription(SinkName));

        try
        {
            return Global.ParseLaunch(description) as Pipeline;
        }
        catch (Exception)
        {
            // A missing element fails the parse
            return null;
        }
    }

    // A pump found its frames cannot be delivered as they come; on its own thread
    private void OnPumpSurfaceFailed(VideoOutputKind kind)
    {
        VideoSurfaceSupport.ReportFailure(ToSurfaceKind(kind),
            kind == VideoOutputKind.D3D11 ? D3D11SharedTextureWriter.LastFailure : null);

        // Reported already when the kind was taken out of the supported ones, but a kind the screen
        // never claimed to support is rebuilt here
        ScheduleRebuild(kind);
    }

    // The screen found a kind does not work; on whatever thread it found out
    private void OnSurfaceFailed(VideoSurfaceKinds kinds)
    {
        Running? running;

        lock (_lock)
            running = _running;

        if (running?.Output.Kind is { } kind && kind != VideoOutputKind.Memory && (ToSurfaceKind(kind) & kinds) != 0)
            ScheduleRebuild(kind);
    }

    private static VideoSurfaceKinds ToSurfaceKind(VideoOutputKind kind) => kind switch
    {
        VideoOutputKind.DmaBuf => VideoSurfaceKinds.DmaBuf,
        VideoOutputKind.Cuda => VideoSurfaceKinds.CudaGl,
        VideoOutputKind.D3D11 => VideoSurfaceKinds.D3D11Shared,
        VideoOutputKind.MemoryYuv => VideoSurfaceKinds.YuvMemory,
        _ => VideoSurfaceKinds.None
    };

    private void ScheduleRebuild(VideoOutputKind kind)
    {
        Running? previous;

        lock (_lock)
        {
            // Only the first report of a pipeline rebuilds it
            if (_isDisposed || _running is not { } running || running.Output.Kind != kind || running.IsReplaced)
                return;

            running.IsReplaced = true;
            previous = running;
        }

        var reason = VideoSurfaceSupport.FailureReason;
        LogSurfaceFailed(_logger, _name, kind, reason);

        // Off the thread of the pump, which disposing the pump waits for
        System.Threading.Tasks.Task.Run(() => Rebuild(previous, ToSurfaceKind(kind), reason));
    }

    private void Rebuild(Running previous, VideoSurfaceKinds failedSurface, string? failureReason)
    {
        try
        {
            lock (_lock)
            {
                if (_running == previous)
                    _running = null;
            }

            previous.Dispose();

            var running = Build(false, failedSurface, failureReason);

            lock (_lock)
            {
                if (_isDisposed)
                {
                    running?.Dispose();
                    return;
                }

                _running = running;

                // A new decoder starts from a keyframe, and everything before one would only break it
                running?.Gate.WaitForKeyFrame();
            }

            if (running == null)
                return;

            LogDecoding(_logger, _name, running.Output.Chain, running.Output.Kind);
            KeyFrameNeeded?.Invoke();
        }
        catch (Exception exception)
        {
            LogRebuildFailed(_logger, exception, _name);
        }
    }

    private void OnMessage(Message message)
    {
        var (error, debug) = message.Type == MessageType.Error ? message.ParseError() : message.ParseWarning();
        LogPipelineMessage(_logger, _name, message.Type, GstNative.GetPath(GstNative.GetMessageSource(message)),
            error.Message, debug);
    }

    public void Dispose()
    {
        Running? running;

        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            running = _running;
            _running = null;
        }

        VideoSurfaceSupport.Failed -= OnSurfaceFailed;
        running?.Dispose();
    }

    /// <summary>One pipeline of the decoder, until it is replaced or the decoder is disposed.</summary>
    private sealed class Running(Pipeline pipeline, Element source, VideoFramePump pump, GstBusWatcher bus,
        VideoOutput output) : IDisposable
    {
        public Element Source { get; } = source;

        public VideoOutput Output { get; } = output;

        public EncodedFrameGate Gate { get; } = new(source, MaxQueuedFrames);

        /// <summary>A rebuild of this pipeline was scheduled.</summary>
        public bool IsReplaced { get; set; }

        public void Dispose()
        {
            // Stopped first: the pump must not pull from a sink that is shutting down
            pump.Dispose();
            pipeline.SetState(State.Null);
            bus.Dispose();
            Source.Dispose();
            pipeline.Dispose();
        }
    }
}
