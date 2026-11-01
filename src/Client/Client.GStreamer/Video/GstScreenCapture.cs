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
    private readonly string _name;
    private readonly VideoCodec _codec;
    private readonly ILogger _logger;
    private readonly Pipeline _pipeline;
    private readonly ScreenEncodingBranch.Result _branch;
    private readonly IDisposable? _resource;
    private readonly Lock _lock = new();

    // Keyframes are made only when asked for: the SFU asks for one when a viewer joins and repeats a
    // request until the keyframe reaches it. A keyframe every 2 s on top of that was a burst every 2 s,
    // which lost packets on a slow uplink, and each loss brought a PLI and one more keyframe
    private readonly Action<EncodedVideoFrame> _onEncodedFrame;

    // The SFU repeats a request until the keyframe reaches it, and every viewer whose picture broke
    // asks as well. Each one answered at once made dozens of keyframes a second (49 PLI in two
    // seconds), and the bursts of those big frames lost the packets that asked for more. So a request
    // while one is on its way is left out, and one that comes this soon after a keyframe waits until
    // this long has passed: that keyframe may be the one that was lost, or came before a new viewer.
    // Under the lock.
    private static readonly TimeSpan MinKeyFrameSpacing = TimeSpan.FromMilliseconds(500);
    private long _lastEncodedKeyFrame;
    private long _keyFrameAsked;
    private volatile bool _isKeyFrameDeferred;

    // What the encoder puts out, logged every few seconds next to what the capture delivers
    private static readonly TimeSpan RateInterval = TimeSpan.FromSeconds(5);
    private long _rateStart;
    private int _encodedFrames;
    private int _encodedKeyFrames;

    // The statistics of the media, taken by the transport every window
    private readonly GstCodecTimer? _encoderTimer;
    private readonly GstBufferCounter? _captureCounter;
    private readonly long _perfStarted = Stopwatch.GetTimestamp();
    private long _perfTaken;
    private long _perfBytes;
    private long _perfFrames;
    private long _perfKeyFrames;
    private long _perfKeyFrameBytes;
    private long _perfMaxKeyFrameBytes;
    private int _targetKbps;

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
        _perfTaken = _perfStarted;

        using (var encoder = branch.Bin.GetByName(ScreenEncodingBranch.EncoderName))
            _encoderTimer = encoder == null ? null : GstCodecTimer.Attach(encoder);

        using (var split = branch.Bin.GetByName(ScreenEncodingBranch.SplitName))
            _captureCounter = split == null ? null : GstBufferCounter.Attach(split, "sink");
    }

    /// <returns>Null when the input cannot be captured or an element is missing.</returns>
    public static GstScreenCapture? Create(VideoInput input, ScreenShareSettings settings, VideoCodec codec,
        string name, ILogger logger)
    {
        // A PipeWire capture keeps its frames on the GPU when the encoder can take them from there
        var isDmaBuf = input is PipeWireVideoInput && ScreenEncodingBranch.CanTakeDmaBuf(codec) &&
            VideoSourceFactory.RgbDmaBufCaps() != null;
        var source = VideoSourceFactory.Create(input, settings.Framerate,
            exception => LogCaptureFailed(logger, exception, name),
            (rate, caps) => LogCaptureRate(logger, name, rate, settings.Framerate, caps), isDmaBuf,
            rate => LogCompositorRate(logger, name, rate));

        if (source == null)
        {
            LogSourceCreateFailed(logger, name, input);
            return null;
        }

        // The screen capture of Windows hands out Direct3D 11 textures
        var branch = ScreenEncodingBranch.Build(codec, settings,
            isD3D11Source: OperatingSystem.IsWindows() && input is MonitorVideoInput or WindowVideoInput,
            isDmaBufSource: isDmaBuf,
            onError: exception => LogBranchParseFailed(logger, exception, name));

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

        using var vaEncoder = isDmaBuf ? branch.Bin.GetByName(ScreenEncodingBranch.EncoderName) : null;
        var sharedVaContext = !isDmaBuf || (vaEncoder != null && GstVaContext.Prepare(pipeline, vaEncoder));
        if (!sharedVaContext)
            LogVaContextFailed(logger, name);
        else if (isDmaBuf)
            LogVaContextReady(logger, name);

        if (!sharedVaContext || !source.Element.Link(branch.Bin))
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

        return new GstScreenCapture(name, codec, pipeline, branch, source.Resource, logger)
        {
            _targetKbps = settings.EffectiveBitrateKbps
        };
    }

    public bool Start()
    {
        lock (_lock)
        {
            if (_isDisposed || _bus != null)
                return false;

            _bus = new GstBusWatcher(_pipeline.GetBus(), MessageType.Error | MessageType.Warning, OnMessage,
                $"capture_{_name}", _logger);

            var loggedEncodedCaps = false;
            _encodedReader = new AppSinkReader(_branch.Encoded, $"encoded_{_name}",
                sample =>
                {
                    if (!loggedEncodedCaps)
                    {
                        using var caps = sample.GetCaps();
                        LogEncodedCaps(_logger, _name, caps?.ToString() ?? "");
                        loggedEncodedCaps = true;
                    }
                    using var buffer = sample.GetBuffer();

                    if (buffer != null && EncodedFrameReady != null)
                        GstEncodedNative.Read(buffer, _onEncodedFrame);
                },
                exception => LogEncodedFrameFailed(_logger, exception, _name));

            _previewPump = new VideoFramePump(_branch.Preview, _branch.PreviewKind, $"preview_{_name}",
                frame => PreviewFrameReady?.Invoke(frame with { Timestamp = -1 }),
                exception => LogPreviewFrameFailed(_logger, exception, _name),
                kind =>
                {
                    var reason = $"Cannot deliver {kind} capture preview.";
                    LogPreviewFrameFailed(_logger, new InvalidOperationException(reason), _name);
                    VideoSurfaceSupport.ReportFailure(VideoSurfaceKinds.DmaBuf, reason);
                });

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
        {
            _encodedKeyFrames++;

            lock (_lock)
            {
                _lastEncodedKeyFrame = Stopwatch.GetTimestamp();
                _keyFrameAsked = 0;
            }
        }
        else if (_isKeyFrameDeferred)
        {
            lock (_lock)
            {
                if (!_isDisposed && _isKeyFrameDeferred &&
                    Stopwatch.GetElapsedTime(_lastEncodedKeyFrame) >= MinKeyFrameSpacing)
                    AskForKeyFrame();
            }
        }

        // The first frame starts the count, so the setup of the pipeline is not counted as time
        // the encoder put out nothing
        if (_rateStart == 0)
            _rateStart = Stopwatch.GetTimestamp();

        _encodedFrames++;
        CountForPerf(frame);

        if (Stopwatch.GetElapsedTime(_rateStart) >= RateInterval)
        {
            LogEncodedRate(_logger, _name, _encodedFrames / Stopwatch.GetElapsedTime(_rateStart).TotalSeconds,
                _encodedKeyFrames);
            _rateStart = Stopwatch.GetTimestamp();
            _encodedFrames = 0;
            _encodedKeyFrames = 0;
        }

        EncodedFrameReady?.Invoke(frame);
    }

    // On the thread of the reader
    private void CountForPerf(EncodedVideoFrame frame)
    {
        Interlocked.Add(ref _perfBytes, frame.Length);

        if (!frame.IsKeyFrame)
        {
            Interlocked.Increment(ref _perfFrames);
            return;
        }

        Interlocked.Increment(ref _perfKeyFrames);
        Interlocked.Add(ref _perfKeyFrameBytes, frame.Length);

        long largest;

        do
        {
            largest = Interlocked.Read(ref _perfMaxKeyFrameBytes);
        } while (frame.Length > largest &&
                 Interlocked.CompareExchange(ref _perfMaxKeyFrameBytes, frame.Length, largest) != largest);
    }

    public EncoderPerf? TakePerf()
    {
        var now = Stopwatch.GetTimestamp();
        var seconds = Stopwatch.GetElapsedTime(Interlocked.Exchange(ref _perfTaken, now), now).TotalSeconds;

        if (seconds <= 0)
            return null;

        var bytes = Interlocked.Exchange(ref _perfBytes, 0);
        var frames = Interlocked.Exchange(ref _perfFrames, 0);
        var keyFrames = Interlocked.Exchange(ref _perfKeyFrames, 0);
        var keyFrameBytes = Interlocked.Exchange(ref _perfKeyFrameBytes, 0);
        var maxKeyFrame = Interlocked.Exchange(ref _perfMaxKeyFrameBytes, 0);
        var captured = _captureCounter?.Take() ?? 0;
        var (input, output, durations) = _encoderTimer?.Take() ?? (0, frames + keyFrames, default);

        return new EncoderPerf(_branch.Encoder.Factory, _branch.Scaler, captured / seconds, input / seconds,
            output / seconds, durations, bytes * 8 / 1000.0 / seconds, Volatile.Read(ref _targetKbps), (int)keyFrames,
            (int)maxKeyFrame, frames > 0 ? (int)Math.Max(0, (bytes - keyFrameBytes) / frames) : 0);
    }

    // Under the lock: the reader disposes the wrapper of the sink when it stops
    public void RequestKeyFrame(bool isUrgent = false)
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            if (!isUrgent)
            {
                // The one asked for answers this request too
                if (_keyFrameAsked != 0 && Stopwatch.GetElapsedTime(_keyFrameAsked) < MinKeyFrameSpacing)
                    return;

                // Made by the next frame after the spacing, see OnEncodedFrame
                if (_lastEncodedKeyFrame != 0 && Stopwatch.GetElapsedTime(_lastEncodedKeyFrame) < MinKeyFrameSpacing)
                {
                    _isKeyFrameDeferred = true;
                    return;
                }
            }

            AskForKeyFrame();
        }
    }

    // Under the lock
    private void AskForKeyFrame()
    {
        _isKeyFrameDeferred = false;
        _keyFrameAsked = Stopwatch.GetTimestamp();
        GstEncodedNative.RequestKeyFrame(_branch.Encoded);
    }

    public void SetBitrate(int kbps)
    {
        lock (_lock)
        {
            if (!_isDisposed)
                ScreenEncodingBranch.SetBitrate(_branch.Bin, _codec, _branch.Encoder, kbps);

            Volatile.Write(ref _targetKbps, kbps);
        }
    }

    public void SetPreviewShown(bool isShown)
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            using var valve = _branch.Bin.GetByName(ScreenEncodingBranch.PreviewValveName);
            valve?.SetProperty("drop", !isShown);
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

        _encoderTimer?.Dispose();

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
