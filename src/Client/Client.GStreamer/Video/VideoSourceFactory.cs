using System.Diagnostics;
using System.Runtime.CompilerServices;
using Client.Application.Ports;
using Gst;

namespace Client.GStreamer.Video;

/// <summary>
/// A capture element together with what has to live as long as it does.
/// </summary>
/// <param name="Resource">
/// What feeds the element from a thread of its own, or null. Released before the element is shut down.
/// </param>
internal sealed record VideoSource(Element Element, IDisposable? Resource = null);

/// <summary>
/// Turns a <see cref="VideoInput"/> into the element that captures it.
/// </summary>
internal static class VideoSourceFactory
{
    // A still screen produces no frames, and an encoder without input cannot answer a keyframe
    // request of a viewer who has just joined
    private const int PipeWireKeepaliveMilliseconds = 100;

    private const string PipeWireElementName = "capture";
    private const string BridgeSinkName = "bridge_out";
    private const string BridgeSourceName = "bridge_in";
    private const string DmaBufFilterName = "dmabuf_caps";

    // pipewiresrc drops DMA_DRM caps without a drm-format list from its offer, so the list is the one
    // the VA postprocessor takes, with the modifiers of this GPU. No memory alternative: BGRx straight
    // into vapostproc came out black
    //
    // Read once, and the probing instance is kept: disposing a fresh vapostproc crashed the client
    private static readonly Lazy<string?> DmaBufCapsOnce = new(ReadDmaBufCaps);

    private static string? DmaBufCaps() => DmaBufCapsOnce.Value;

    private static string? ReadDmaBufCaps()
    {
        var postprocessor = ElementFactory.Make("vapostproc", null);
        var pad = postprocessor?.GetStaticPad("sink");
        var caps = pad?.QueryCaps(null);

        var structures = caps?.ToString().Split(';', StringSplitOptions.TrimEntries)
            .Where(structure => structure.Contains("DMA_DRM", StringComparison.Ordinal) &&
                                structure.Contains("drm-format", StringComparison.Ordinal))
            .ToArray();

        // Set on the element, not written into the description: the launch parser chokes on this list
        return structures is { Length: > 0 } ? string.Join("; ", structures) : null;
    }

    // The capture chain ends in a sink of its own, see CaptureBridge. The rate is cut here already:
    // a compositor delivers a frame per screen refresh, far more than is sent. Only dropped, never
    // duplicated: a plain videorate fills the time before the first frame and every gap with copies,
    // thousands of full-size frames at the start of a share. A drop-only videorate refuses a fixed
    // framerate above the one of the capture, so none is set; plain video/x-raw keeps the frames in
    // memory for a send branch that cannot take DMA-BUF. One that can leaves the format to the compositor.
    //
    // The buffers keep the timestamps of the capture, both halves run on the clock of one pipeline.
    //
    // A frame in memory is copied out of the buffer of PipeWire, which goes back to the compositor at
    // once. KWin hands a stream only a few buffers, and the queues, the videorates and the bridge hold
    // more than that: the compositor had none free for most of its frames, a share at 144 came out
    // at 35 to 60 fps. A DMA-BUF frame stays as it is, its copy would leave the GPU; it would starve
    // the compositor the same way, which is one more reason its capture is still off.
    //
    // The rate is cut right at the source, so what is dropped goes back at once and only the frames
    // that are sent wait in the queue and get converted. The copy still takes every frame the
    // compositor hands out: pipewiresrc makes it before anything here sees the frame.
    private const string PipeWireDescription =
        "pipewiresrc name=" + PipeWireElementName + " do-timestamp=true keepalive-time={0} always-copy={4} ! " +
        "videorate drop-only=true max-rate={1} ! queue max-size-buffers=2 leaky=downstream ! {3}{2}" +
        "appsink name=" + BridgeSinkName + " max-buffers=2 drop=true wait-on-eos=false sync=false async=false " +

        "appsrc name=" + BridgeSourceName + " is-live=true format=time max-buffers=2 leaky-type=downstream";

    /// <param name="onError">
    /// Receives the failures of a source that hands its frames over on a thread of its own.
    /// </param>
    /// <returns>Null when the element is missing from the runtime or the input cannot be captured here.</returns>
    /// <param name="onRate">Receives the rate and the caps of a capture every few seconds.</param>
    /// <param name="acceptDmaBuf">The send branch takes DMA-BUF frames, so a capture may keep them on the GPU.</param>
    /// <param name="onSourceRate">
    /// Receives every few seconds how many frames per second the compositor hands out, before any is
    /// dropped here. PipeWire only.
    /// </param>
    public static VideoSource? Create(VideoInput input, int framerate, Action<Exception> onError,
        Action<double, string>? onRate = null, bool acceptDmaBuf = false,
        Action<double>? onSourceRate = null) => input switch
    {
        PipeWireVideoInput pipeWire => CreatePipeWire(pipeWire, framerate, onError, onRate, acceptDmaBuf, onSourceRate),
        MonitorVideoInput monitor => CreateScreenCapture("monitor-handle", monitor.Handle, isWindow: false, framerate, onRate),
        WindowVideoInput window => CreateScreenCapture("window-handle", window.Handle, isWindow: true, framerate, onRate),
        TestPatternVideoInput => TestPatternSource.Create(framerate),
        _ => null
    };

    private static VideoSource? CreatePipeWire(PipeWireVideoInput input, int framerate, Action<Exception> onError,
        Action<double, string>? onRate, bool acceptDmaBuf, Action<double>? onSourceRate)
    {
        if (input.Remote.IsInvalid || input.Remote.IsClosed)
            return null;

        var dmaBufCaps = acceptDmaBuf ? DmaBufCaps() : null;
        acceptDmaBuf = dmaBufCaps != null;

        // The only open end is the output of the appsrc, which becomes the output of the bin
        var bin = Global.ParseBinFromDescription(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            PipeWireDescription, PipeWireKeepaliveMilliseconds, framerate,
            dmaBufCaps != null ? "capsfilter name=" + DmaBufFilterName + " ! " : "video/x-raw ! ", acceptDmaBuf ? "" : "videoconvert ! ",
            acceptDmaBuf ? "false" : "true"), true);

        if (bin?.GetByName(PipeWireElementName) is not { } source ||
            bin.GetByName(BridgeSinkName) is not { } bridgeSink ||
            bin.GetByName(BridgeSourceName) is not { } bridgeSource)
        {
            bin?.Dispose();
            return null;
        }

        if (dmaBufCaps != null && bin.GetByName(DmaBufFilterName) is { } filter)
        {
            Global.UtilSetObjectArg(filter, "caps", dmaBufCaps);
            filter.Dispose();
        }

        // The element duplicates the descriptor, so ours stays usable for the next start
        source.SetProperty("fd", input.Remote.DangerousGetHandle().ToInt32());
        source.SetProperty("path", input.NodeId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // The probe delegate is kept as long as the element is, so the wrapper stays undisposed
        // A keepalive sends the last frame again, as the same buffer: it is not counted, a still
        // screen gets no frames from the compositor
        if (onSourceRate != null)
            WatchRate(source, (rate, _) => onSourceRate(rate),
                TimeSpan.FromMilliseconds(PipeWireKeepaliveMilliseconds * 0.9));
        else
            source.Dispose();

        // pipewiresrc offers DMA_DRM only to a sink that announces GstVideoMeta, which appsink does not
        if (acceptDmaBuf)
            Interop.GstVideoNative.AcceptVideoMeta(bridgeSink);

        return new VideoSource(bin, new CaptureBridge(bridgeSink, bridgeSource, onError, onRate));
    }

    // d3d11screencapturesrc paces itself at the rate it negotiates, and with nothing downstream to
    // fix one it settles on 30/1. The drop-only videorate of the send branch cannot raise that, so
    // the rate is fixed right at the capture. Both memory kinds stay open: the CPU branch takes
    // system memory, the Direct3D 11 one textures
    private const string ScreenCaptureDescription =
        "d3d11screencapturesrc name=" + ScreenCaptureElementName + " ! capsfilter name=" + ScreenCaptureFilterName;

    private const string ScreenCaptureElementName = "capture";
    private const string ScreenCaptureFilterName = "capture_rate";

    private static VideoSource? CreateScreenCapture(string handleProperty, IntPtr handle, bool isWindow,
        int framerate, Action<double, string>? onRate)
    {
        if (!OperatingSystem.IsWindows() || handle == IntPtr.Zero)
            return null;

        // The only open end is the output of the capsfilter, which becomes the output of the bin
        var bin = Global.ParseBinFromDescription(ScreenCaptureDescription, true);

        if (bin?.GetByName(ScreenCaptureElementName) is not { } source ||
            bin.GetByName(ScreenCaptureFilterName) is not { } filter)
        {
            bin?.Dispose();
            return null;
        }

        Global.UtilSetObjectArg(filter, "caps", string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "video/x-raw(memory:D3D11Memory),framerate={0}/1; video/x-raw,framerate={0}/1", framerate));
        filter.Dispose();

        source.SetProperty(handleProperty, (ulong)handle.ToInt64());
        source.SetProperty("show-cursor", true);
        source.SetProperty("do-timestamp", true);

        // Desktop duplication only sees whole monitors, a single window needs Windows Graphics Capture
        if (isWindow)
            Global.UtilSetObjectArg(source, "capture-api", "wgc");

        // The probe delegate is kept as long as the element is, so the wrapper stays undisposed
        if (onRate != null)
            WatchRate(source, onRate);

        return new VideoSource(bin);
    }

    // Keeps each probe delegate alive as long as the element it watches
    private static readonly ConditionalWeakTable<Element, PadProbeCallback> RateProbes = new();

    private static void WatchRate(Element source, Action<double, string> onRate) =>
        WatchRate(source, (rate, pad) =>
        {
            using var caps = pad.GetCurrentCaps();
            onRate(rate, caps?.ToString() ?? "");
        }, repeatAfter: null);

    /// <param name="repeatAfter">
    /// Leaves out a buffer pushed again right after itself at least this long later. The time tells it
    /// from a new frame that got the memory of the one before, freed in the meantime.
    /// </param>
    private static void WatchRate(Element source, Action<double, Pad> onRate, TimeSpan? repeatAfter)
    {
        using var pad = source.GetStaticPad("src");

        if (pad == null)
            return;

        var clock = Stopwatch.StartNew();
        var frames = 0;
        var last = IntPtr.Zero;
        var lastAt = 0L;

        PadProbeCallback callback = (probedPad, info) =>
        {
            if (repeatAfter is { } interval)
            {
                using var buffer = info.GetBuffer();
                var current = buffer?.Handle ?? IntPtr.Zero;

                if (current != last || Stopwatch.GetElapsedTime(lastAt) < interval)
                    frames++;

                last = current;
                lastAt = Stopwatch.GetTimestamp();
            }
            else
                frames++;

            if (clock.Elapsed < TimeSpan.FromSeconds(5))
                return PadProbeReturn.Ok;

            onRate(frames / clock.Elapsed.TotalSeconds, probedPad);
            frames = 0;
            clock.Restart();

            return PadProbeReturn.Ok;
        };

        RateProbes.AddOrUpdate(source, callback);
        pad.AddProbe(PadProbeType.Buffer, callback);
    }
}
