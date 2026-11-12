using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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

    internal static string? DmaBufCaps() => DmaBufCapsOnce.Value;

    // Keep the compositor's RGB layout and modifiers until VA imports the frame.
    internal static string? RgbDmaBufCaps()
    {
        var caps = DmaBufCaps();
        if (caps == null) return null;
        var formats = Regex.Matches(caps, @"\b(?:AR24|XR24|AB24|XB24)(?::0x[0-9a-fA-F]+)?")
            .Select(match => match.Value).Distinct().ToArray();
        return formats.Length == 0 ? null :
            "video/x-raw(memory:DMABuf),format=DMA_DRM,drm-format=(string){" + string.Join(',', formats) + "}";
    }

    private static readonly Lazy<string?> DmaBufExportCapsOnce = new(() => ReadDmaBufCaps("src"));

    internal static string? Nv12DmaBufExportCaps()
    {
        var caps = DmaBufExportCapsOnce.Value;
        if (caps == null) return null;
        var formats = Regex.Matches(caps, @"\bNV12(?::0x[0-9a-fA-F]+)?")
            .Select(match => match.Value).Distinct().ToArray();
        return formats.Length == 0 ? null :
            "video/x-raw(memory:DMABuf),format=DMA_DRM,drm-format=(string){" + string.Join(',', formats) + "}";
    }

    private static string? ReadDmaBufCaps() => ReadDmaBufCaps("sink");

    private static string? ReadDmaBufCaps(string padName)
    {
        var postprocessor = ElementFactory.Make("vapostproc", null);
        var pad = postprocessor?.GetStaticPad(padName);
        var caps = pad?.QueryCaps(null);

        var structures = caps?.ToString().Split(';', StringSplitOptions.TrimEntries)
            .Where(structure => structure.Contains("DMA_DRM", StringComparison.Ordinal) &&
                                structure.Contains("drm-format", StringComparison.Ordinal))
            .ToArray();

        // Set on the element, not written into the description: the launch parser chokes on this list
        return structures is { Length: > 0 } ? string.Join("; ", structures) : null;
    }

    // Memory capture keeps its copy before the queues. GPU capture instead converts RGB
    // into an owned NV12 VA surface before the bridge: neither the encoder nor the preview
    // may retain one of the compositor's scarce buffers. Rate limiting follows that import.
    private const string PipeWireDescription =
        "pipewiresrc name=" + PipeWireElementName + " do-timestamp=true min-buffers=1 keepalive-time={0} always-copy=true ! " +
        "videorate drop-only=true max-rate={1} ! queue max-size-buffers=2 leaky=downstream ! videoconvert ! video/x-raw ! " +
        "appsink name=" + BridgeSinkName + " max-buffers=2 drop=true enable-last-sample=false wait-on-eos=false sync=false async=false " +
        "appsrc name=" + BridgeSourceName + " is-live=true format=time max-buffers=2 leaky-type=downstream";

    private const string PipeWireDmaBufDescription =
        "pipewiresrc name=" + PipeWireElementName + " do-timestamp=true min-buffers=1 keepalive-time={0} always-copy=false ! " +
        "capsfilter name=" + DmaBufFilterName + " ! vapostproc ! video/x-raw(memory:VAMemory),format=NV12 ! " +
        "appsink name=" + BridgeSinkName + " max-buffers=1 drop=true enable-last-sample=false wait-on-eos=false sync=false async=false processing-deadline=0 " +
        "appsrc name=" + BridgeSourceName + " is-live=true format=time max-buffers=1 leaky-type=downstream";

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
        Action<double>? onSourceRate = null, int maxHeight = 1080) => input switch
    {
        PipeWireVideoInput pipeWire => CreatePipeWire(pipeWire, framerate, onError, onRate, acceptDmaBuf, onSourceRate),
        MonitorVideoInput monitor => CreateScreenCapture("monitor-handle", monitor.Handle, isWindow: false, framerate, onRate),
        WindowVideoInput window => CreateScreenCapture("window-handle", window.Handle, isWindow: true, framerate, onRate),
        CameraVideoInput camera => CreateCamera(camera, maxHeight, framerate),
        TestPatternVideoInput => TestPatternSource.Create(framerate),
        _ => null
    };

    private static VideoSource? CreateCamera(CameraVideoInput input, int height, int framerate)
    {
        using var source = GstCameraDeviceProvider.CreateSource(input.DeviceId, out var formats);
        if (source == null)
            return null;
        return CreateCamera(source, formats == null ? null : SelectCameraCaps(formats, height, framerate));
    }

    internal static string? SelectCameraCaps(string formats, int height, int framerate)
    {
        using var supported = Caps.FromString(formats);
        var width = (height * 16 / 9 + 1) & ~1;
        // Prefer the requested mode, then its frame rate, then any supported raw or MJPEG mode.
        // This avoids a USB camera choosing a high-resolution raw mode limited to a few FPS.
        var preferred = $"video/x-raw,width={width},height={height},framerate={framerate}/1; " +
            $"image/jpeg,width={width},height={height},framerate={framerate}/1; " +
            $"video/x-raw,framerate={framerate}/1; image/jpeg,framerate={framerate}/1; video/x-raw; image/jpeg";
        using var wanted = Caps.FromString(preferred);
        if (wanted == null || supported == null) return null;
        using var intersection = wanted.IntersectFull(supported, CapsIntersectMode.First);
        if (intersection.IsEmpty()) return null;
        using var fixedCaps = intersection.Fixate();
        return fixedCaps?.ToString();
    }

    internal static VideoSource? CreateCamera(Element source, string? caps = null)
    {
        // decodebin accepts both raw cameras and cameras which expose MJPEG. The source and
        // decoder negotiate a native mode; the shared encoding branch limits size and rate.
        var bin = Global.ParseBinFromDescription("capsfilter name=camera_mode ! decodebin ! videoconvert", true);
        if (bin == null)
            return null;
        using var sink = bin.GetStaticPad("sink");
        if (sink != null)
            bin.RemovePad(sink);
        bin.Add(source);
        using var decoder = bin.GetByName("camera_mode");
        if (decoder != null && caps != null)
            Global.UtilSetObjectArg(decoder, "caps", caps);
        if (decoder == null || !source.Link(decoder))
        {
            bin.Dispose();
            return null;
        }
        return new VideoSource(bin);
    }

    private static VideoSource? CreatePipeWire(PipeWireVideoInput input, int framerate, Action<Exception> onError,
        Action<double, string>? onRate, bool acceptDmaBuf, Action<double>? onSourceRate)
    {
        if (input.Remote.IsInvalid || input.Remote.IsClosed)
            return null;

        var dmaBufCaps = acceptDmaBuf ? RgbDmaBufCaps() : null;
        acceptDmaBuf = dmaBufCaps != null;

        // The only open end is the output of the appsrc, which becomes the output of the bin
        var bin = Global.ParseBinFromDescription(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            acceptDmaBuf ? PipeWireDmaBufDescription : PipeWireDescription,
            PipeWireKeepaliveMilliseconds, framerate), true);

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

        return new VideoSource(bin, new CaptureBridge(bridgeSink, bridgeSource, onError, onRate, framerate,
            setEncoderFramerate: acceptDmaBuf));
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
