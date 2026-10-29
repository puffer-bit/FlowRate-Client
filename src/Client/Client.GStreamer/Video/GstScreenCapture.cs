using System.Diagnostics;
using Client.Application.Ports;
using Client.Core.Media;
using Client.GStreamer.Interop;
using Gst;
using Microsoft.Extensions.Logging;

namespace Client.GStreamer.Video;

/// <summary>
/// The capture of one shown screen in a pipeline of its own: the source of the input, then the
/// <see cref="ScreenEncodingBranch"/>. The encoded frames and the preview are read out of their sinks
/// on threads of their own.
/// </summary>
internal sealed partial class GstScreenCapture : IScreenCapture
{
    // Off until it works: pipewiresrc 1.2.7 fails on the DMA_DRM format KWin picks ("unhandled format"),
    // and BGRx memory straight into vapostproc comes out black
    private static readonly bool IsDmaBufCaptureEnabled = false;

    private readonly string _name;
    private readonly VideoCodec _codec;
    private readonly ILogger _logger;
    private readonly Pipeline _pipeline;
    private readonly ScreenEncodingBranch.Result _branch;
    private readonly IDisposable? _resource;
    private readonly Lock _lock = new();

    // AMF in its low-latency usages makes a keyframe only when asked for one, whatever its GOP size, and
    // the requests of the SFU may not come through: a viewer who joins, or whose decoder dropped the
    // frames up to a keyframe, would wait until the capture starts over. One is asked for after this long
    private static readonly TimeSpan KeyFrameInterval = TimeSpan.FromSeconds(2);
    private long _lastKeyFrame = Stopwatch.GetTimestamp();
    private readonly Action<EncodedVideoFrame> _onEncodedFrame;

    private GstBusWatcher? _bus;
    private AppSinkReader? _encodedReader;
    private VideoFramePump? _previewPump;
    private bool _isDisposed;

    public string Description { get; }

    public event Action<RawVideoFrame>? PreviewFrameReady;
    public event Action<EncodedVideoFrame>? EncodedFrameReady;

    private GstScreenCapture(string name, VideoCodec codec, Pipeline pipeline, ScreenEncodingBranch.Result branch,
        IDisposable? resource, ILogger logger)
    {
        _name = name;
        _codec = codec;
        _pipeline = pipeline;
        _branch = branch;
        _resource = resource;
        _logger = logger;

        Description = $"{branch.Encoder.ToLaunchString()}, scaled on {branch.Scaler}";
        _onEncodedFrame = OnEncodedFrame;
    }

    /// <returns>Null when the input cannot be captured or an element is missing.</returns>
    public static GstScreenCapture? Create(VideoInput input, ScreenShareSettings settings, VideoCodec codec,
        string name, ILogger logger)
    {
        // A PipeWire capture keeps its frames on the GPU when the encoder can take them from there
        var isDmaBuf = IsDmaBufCaptureEnabled && input is PipeWireVideoInput && ScreenEncodingBranch.CanTakeDmaBuf(codec);
        var source = VideoSourceFactory.Create(input, settings.Framerate,
            exception => LogCaptureFailed(logger, exception, name),
            (rate, caps) => LogCaptureRate(logger, name, rate, settings.Framerate, caps), isDmaBuf);

        if (source == null)
        {
            LogSourceCreateFailed(logger, name, input);
            return null;
        }

        // The screen capture of Windows hands out Direct3D 11 textures
        var branch = ScreenEncodingBranch.Build(codec, settings,
            isD3D11Source: OperatingSystem.IsWindows() && input is MonitorVideoInput or WindowVideoInput,
            isDmaBufSource: isDmaBuf);

        if (branch == null)
        {
            source.Resource?.Dispose();
            source.Element.Dispose();
            LogBranchBuildFailed(logger, name);
            return null;
        }

        var pipeline = Pipeline.New($"capture_{name}");
        pipeline.Add(source.Element);
        pipeline.Add(branch.Bin);

        if (!source.Element.Link(branch.Bin))
        {
            LogBranchBuildFailed(logger, name);
            source.Resource?.Dispose();
            branch.Preview.Dispose();
            branch.Encoded.Dispose();
            pipeline.SetState(State.Null);
            pipeline.Dispose();
            return null;
        }

        // The pipeline holds them from here on
        source.Element.Dispose();

        return new GstScreenCapture(name, codec, pipeline, branch, source.Resource, logger);
    }

    public bool Start()
    {
        lock (_lock)
        {
            if (_isDisposed || _bus != null)
                return false;

            _bus = new GstBusWatcher(_pipeline.GetBus(), MessageType.Error | MessageType.Warning, OnMessage,
                $"capture_{_name}", _logger);

            _encodedReader = new AppSinkReader(_branch.Encoded, $"encoded_{_name}",
                sample =>
                {
                    using var buffer = sample.GetBuffer();

                    if (buffer != null && EncodedFrameReady != null)
                        GstEncodedNative.Read(buffer, _onEncodedFrame);
                },
                exception => LogEncodedFrameFailed(_logger, exception, _name));

            _previewPump = new VideoFramePump(_branch.Preview, VideoOutputKind.Memory, $"preview_{_name}",
                frame => PreviewFrameReady?.Invoke(frame),
                exception => LogPreviewFrameFailed(_logger, exception, _name));

            if (_pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                LogStartFailed(_logger, _name);
                return false;
            }
        }

        LogStarted(_logger, _name, Description);
        return true;
    }

    // On the thread of the reader
    private void OnEncodedFrame(EncodedVideoFrame frame)
    {
        if (frame.IsKeyFrame)
            _lastKeyFrame = Stopwatch.GetTimestamp();
        else if (Stopwatch.GetElapsedTime(_lastKeyFrame) >= KeyFrameInterval)
        {
            // Asked for once per interval: the keyframe takes a few frames to come
            _lastKeyFrame = Stopwatch.GetTimestamp();
            RequestKeyFrame();
        }

        EncodedFrameReady?.Invoke(frame);
    }

    // Under the lock: the reader disposes the wrapper of the sink when it stops
    public void RequestKeyFrame()
    {
        lock (_lock)
        {
            if (!_isDisposed)
                GstEncodedNative.RequestKeyFrame(_branch.Encoded);
        }
    }

    public void SetBitrate(int kbps)
    {
        lock (_lock)
        {
            if (!_isDisposed)
                ScreenEncodingBranch.SetBitrate(_branch.Bin, _codec, _branch.Encoder, kbps);
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
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
        }

        // The readers and the feeding threads work on elements of the pipeline, so they stop first
        _encodedReader?.Dispose();
        _previewPump?.Dispose();
        _resource?.Dispose();

        _pipeline.SetState(State.Null);
        _bus?.Dispose();

        if (_encodedReader == null)
            _branch.Encoded.Dispose();

        if (_previewPump == null)
            _branch.Preview.Dispose();

        _branch.Bin.Dispose();
        _pipeline.Dispose();
    }
}
