using System;
using System.Diagnostics;
using System.Threading;
using Client.Application.Ports;
using Client.Core.Media;
using Client.GStreamer.Interop;
using Client.GStreamer.Video;
using Gst;
using Microsoft.Extensions.Logging;

namespace Client.Avalonia.Video.Preview;

/// <summary>A direct PipeWire capture for measuring the source without the send branch.</summary>
internal sealed class PipeWireFeed : IDisposable
{
    private readonly Pipeline _pipeline;
    private readonly Element _source;
    private readonly PadProbeCallback _sourceProbe;
    private readonly VideoFramePump? _pump;
    private readonly GstBusWatcher _bus;

    public PipeWireFeed(PipeWireVideoInput input, int framerate, Action<RawVideoFrame> onFrame,
        Action<double, string> onRate, Action<double> onSourceRate, Action<Exception> onError, ILogger logger,
        bool copyBuffers = false, bool captureOnly = false, bool dmaBufCapture = false)
    {
        _pipeline = Pipeline.New("pipewire_preview");
        try
        {
            // Let the compositor run at the display refresh (165/180 Hz included).
            // Limit presentation below using a fractional schedule without retaining
            // another PipeWire buffer in videorate.
            // Without a source copy, keep no intermediate queue or last sample: each
            // retained buffer is one the compositor cannot use for the next screen update.
            var queue = copyBuffers
                ? "queue max-size-buffers=1 max-size-bytes=0 max-size-time=0 leaky=downstream ! "
                : "";
            // The diagnostic path ends immediately at fakesink: no mapping, conversion,
            // managed frame delivery or renderer can apply backpressure to the source.
            var dmaBufCaps = dmaBufCapture ? VideoSourceFactory.DmaBufCaps() : null;
            if (dmaBufCapture && dmaBufCaps == null)
                throw new InvalidOperationException("DMA-BUF capture needs GPU format capabilities.");
            if (dmaBufCapture && !captureOnly)
            {
                if (!VideoSurfaceSupport.Supports(VideoSurfaceKinds.DmaBuf))
                    throw new InvalidOperationException("The video presenter cannot import DMA-BUF frames.");
                dmaBufCaps = VideoSourceFactory.RgbDmaBufCaps()
                    ?? throw new InvalidOperationException("No supported RGB DMA-BUF formats were advertised.");
            }
            if (dmaBufCapture) copyBuffers = false;
            var output = captureOnly
                ? "fakesink name=preview sync=false async=false enable-last-sample=false"
                : (dmaBufCapture ? "" : queue + $"videoconvert n-threads={Math.Clamp(Environment.ProcessorCount / 2, 1, 8)} ! " +
                  VideoFramePump.FrameCaps + " ! ") +
                  "appsink name=preview max-buffers=1 drop=true enable-last-sample=false " +
                  "wait-on-eos=false sync=false async=false processing-deadline=0";
            // A scalar max-framerate is an exact SPA constraint, not a ceiling.
            // A range intersects the compositor's native maximum (e.g. 165 Hz).
            var sourceCaps = dmaBufCapture
                ? "capsfilter name=source_caps ! "
                : $"video/x-raw,max-framerate=(fraction)[0/1,{Math.Max(240, framerate)}/1] ! ";
            using var capture = Global.ParseBinFromDescription(
                $"pipewiresrc name=capture do-timestamp=true min-buffers=1 always-copy={copyBuffers.ToString().ToLowerInvariant()} keepalive-time=0 ! " +
                sourceCaps + output, true)
                ?? throw new InvalidOperationException("Cannot create PipeWire preview.");
            _pipeline.Add(capture);
            if (dmaBufCaps != null)
            {
                using var filter = capture.GetByName("source_caps")
                    ?? throw new InvalidOperationException("Missing DMA-BUF caps filter.");
                Global.UtilSetObjectArg(filter, "caps", dmaBufCaps);
                using var diagnosticSink = capture.GetByName("preview")
                    ?? throw new InvalidOperationException("Missing diagnostic sink.");
                GstVideoNative.AcceptVideoMeta(diagnosticSink);
                logger.LogInformation("PipeWire DMA-BUF capture: GPU buffers only, no CPU readback; offered caps: {Caps}", dmaBufCaps);
            }
            _source = capture.GetByName("capture") ?? throw new InvalidOperationException("Missing PipeWire source.");
            _source.SetProperty("fd", input.Remote.DangerousGetHandle().ToInt32());
            _source.SetProperty("path", input.NodeId.ToString(System.Globalization.CultureInfo.InvariantCulture));

            logger.LogInformation("PipeWire preview: presentation limit {Rate} FPS, copy buffers: {Copy}, capture only: {CaptureOnly}",
                framerate, copyBuffers, captureOnly);
            var received = 0;
            var delivered = 0;
            var clock = Stopwatch.StartNew();
            var handoffMilliseconds = 0.0;
            var nextPresentation = 0.0;
            var presentationInterval = (double)Stopwatch.Frequency / framerate;
            var presented = 0;
            _sourceProbe = (sourcePad, _) =>
            {
                Interlocked.Increment(ref received);
                if (captureOnly && clock.Elapsed.TotalSeconds >= 2)
                {
                    var rate = Interlocked.Exchange(ref received, 0) / clock.Elapsed.TotalSeconds;
                    using var caps = sourcePad.GetCurrentCaps();
                    onSourceRate(rate);
                    onRate(rate, caps?.ToString() ?? "");
                    logger.LogInformation("PipeWire capture-only (fakesink): {Rate:F1} FPS; caps: {Caps}", rate, caps?.ToString());
                    clock.Restart();
                }
                return PadProbeReturn.Ok;
            };
            using (var pad = _source.GetStaticPad("src")
                ?? throw new InvalidOperationException("Missing PipeWire source pad."))
                pad.AddProbe(PadProbeType.Buffer, _sourceProbe);
            if (!captureOnly)
            {
                var sink = capture.GetByName("preview") ?? throw new InvalidOperationException("Missing preview sink.");
                _pump = new VideoFramePump(sink, dmaBufCapture ? VideoOutputKind.DmaBuf : VideoOutputKind.Memory, "pipewire_preview", frame =>
                {
                    if (dmaBufCapture && !VideoSurfaceSupport.Supports(VideoSurfaceKinds.DmaBuf))
                        throw new InvalidOperationException(VideoSurfaceSupport.FailureReason ?? "OpenGL DMA-BUF import failed.");
                    var handoffStarted = Stopwatch.GetTimestamp();
                    if (handoffStarted >= nextPresentation)
                    {
                        // Live capture uses the latest frame. A network jitter buffer
                        // would pin the compositor's small DMA-BUF pool unnecessarily.
                        onFrame(dmaBufCapture ? frame with { Timestamp = -1 } : frame);
                        presented++;
                        // Keep the fractional remainder: anchoring every deadline to now
                        // would also halve 165 Hz when the target is 144 Hz.
                        nextPresentation = nextPresentation == 0
                            ? handoffStarted + presentationInterval
                            : Math.Max(nextPresentation + presentationInterval, handoffStarted);
                    }
                    handoffMilliseconds += Stopwatch.GetElapsedTime(handoffStarted).TotalMilliseconds;
                    delivered++;
                    if (clock.Elapsed.TotalSeconds < 2) return;
                    var seconds = clock.Elapsed.TotalSeconds;
                    var sourceFrames = Interlocked.Exchange(ref received, 0);
                    var sourceRate = sourceFrames / seconds;
                    var previewRate = delivered / seconds;
                    using var pad = sink.GetStaticPad("sink")
                        ?? throw new InvalidOperationException("Missing preview sink pad.");
                    using var caps = pad.GetCurrentCaps();
                    onSourceRate(sourceRate);
                    onRate(previewRate, caps?.ToString() ?? "");
                    logger.LogInformation("PipeWire source: {Source:F1} FPS; preview input: {Preview:F1} FPS; submitted to VideoView: {Submitted:F1} FPS; skipped before preview: {Skipped}; VideoView handoff: {Handoff:F2} ms/frame; caps: {Caps}",
                        sourceRate, previewRate, presented / seconds, Math.Max(0, sourceFrames - delivered), handoffMilliseconds / delivered, caps?.ToString());
                    handoffMilliseconds = 0;
                    delivered = 0;
                    presented = 0;
                    clock.Restart();
                }, onError, kind => onError(new InvalidOperationException($"Cannot deliver {kind} PipeWire frames.")));
            }
            _bus = new GstBusWatcher(_pipeline.GetBus(), MessageType.Error | MessageType.Warning,
                message =>
                {
                    var (error, debug) = message.Type == MessageType.Error ? message.ParseError() : message.ParseWarning();
                    if (message.Type == MessageType.Error)
                        onError(new InvalidOperationException($"{error.Message}: {debug}"));
                    else
                        logger.LogWarning("{Message}: {Debug}", error.Message, debug);
                }, "pipewire_preview", logger);
            if (_pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
                throw new InvalidOperationException("PipeWire pipeline failed to start.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _pump?.Dispose();
        _pipeline.SetState(State.Null);
        _bus?.Dispose();
        _source?.Dispose();
        _pipeline.Dispose();
    }
}
