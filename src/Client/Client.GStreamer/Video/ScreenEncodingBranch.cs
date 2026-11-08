using System.Globalization;
using Gst;
using Client.Core.Media;

namespace Client.GStreamer.Video;

/// <summary>
/// What a shown screen goes through after its capture: the picture is paced and scaled, and encoded
/// into a sink of encoded frames. A second leg feeds a sink with raw frames, so the sender sees what
/// is being sent.
/// </summary>
internal static class ScreenEncodingBranch
{
    // The sharer sees the capture at its own size and rate
    private const int PreviewMaxHeight = 16384;

    public const string PreviewSinkName = "preview";

    // Closed while nobody looks at the preview to avoid conversion and presentation work
    public const string PreviewValveName = "preview_valve";
    public const string EncodedSinkName = "encoded";
    public const string EncoderName = "encoder";

    // Where the capture comes in after the pacing, for the rate it delivers
    public const string SplitName = "split";

    // videorate turns the uneven capture into a steady rate, which the rate control of the encoders
    // relies on. Both legs drop instead of queueing: a late frame of a screen is worth nothing.
    //
    // Each leg scales for itself, after the tee: a limit placed before it is negotiated against
    // both legs, and the smaller preview would then shrink what is sent as well.
    //
    // The encoded picture ends in {3}, described by VideoCodecPipeline.
    private const string Description =
        "queue max-size-buffers=2 leaky=downstream ! " +
        "videorate drop-only=true max-rate={0} ! tee name=split " +

        "split. ! queue max-size-buffers=1 leaky=downstream ! {6}" +
        "{2} ! " +
        "{3} " +

        "split. ! valve name=" + PreviewValveName + " ! queue max-size-buffers=1 leaky=downstream ! " +
        "videoscale ! videoconvert ! " +
        VideoFramePump.FrameCaps + ",height=[2,{4}],pixel-aspect-ratio=1/1 ! {5}";

    // CaptureBridge already limits the variable-rate PipeWire stream. Do not pace it
    // again or retain another GPU surface before the tee. Each consumer has its own
    // single-frame queue; the encoder's output is never dropped.
    private const string VaDmaBufDescription =
        "tee name=split " +

        "split. ! queue max-size-buffers=1 leaky=downstream ! {6}" +
        "{2} ! " +
        "{3} " +

        "split. ! valve name=" + PreviewValveName + " ! queue max-size-buffers=1 leaky=downstream ! " +
        "{7} ! {5}";

    // The same for a Direct3D 11 capture on Windows: the frames stay textures from the capture to the
    // encoder. d3d11convert scales and converts for the encoder, and the preview is scaled down on the
    // GPU before it is downloaded
    private const string D3D11Description =
        "queue max-size-buffers=2 leaky=downstream ! video/x-raw(memory:D3D11Memory) ! " +
        "videorate drop-only=true max-rate={0} ! tee name=split " +

        "split. ! queue max-size-buffers=1 leaky=downstream ! " +
        "d3d11convert ! video/x-raw(memory:D3D11Memory),format=NV12,height=[2,{1}],pixel-aspect-ratio=1/1 ! " +
        "{2} ! " +
        "{3} " +

        "split. ! valve name=" + PreviewValveName + " ! queue max-size-buffers=1 leaky=downstream ! " +
        "d3d11convert ! video/x-raw(memory:D3D11Memory),format=BGRA,height=[2,{4}],pixel-aspect-ratio=1/1 ! " +
        "d3d11download ! " + VideoFramePump.FrameCaps + " ! {5}";

    // Hardware encoders of Windows that take Direct3D 11 textures as they are
    private static readonly string[] D3D11Encoders =
        ["nvd3d11h264enc", "amfh264enc", "qsvh264enc", "mfh264enc", "nvd3d11av1enc", "amfav1enc", "qsvav1enc"];

    // The sink never drops:
    // a lost frame would break every one up to the next keyframe; it holds the encoder back instead
    private const string EncodedSink =
        "appsink name=" + EncodedSinkName + " max-buffers=8 drop=false enable-last-sample=false wait-on-eos=false sync=false async=false";

    // Scales and converts for the encoder: on the GPU where the encoder is a VA-API one, whose
    // postprocessor does both, otherwise on the CPU
    private const string CpuScale = "videoscale ! video/x-raw,height=[2,{0}],pixel-aspect-ratio=1/1 ! videoconvert ! ";
    private const string VaScale = "vapostproc ! video/x-raw(memory:VAMemory),height=[2,{0}],pixel-aspect-ratio=1/1 ! ";

    /// <param name="Bin">A bin with a single sink pad, which ends in its two sinks.</param>
    /// <param name="Preview">Sink of the preview leg, for a <see cref="VideoFramePump"/>.</param>
    /// <param name="Encoded">Sink of the encoded frames.</param>
    /// <param name="Scaler">Where the picture is scaled and converted for the encoder, for the log.</param>
    public sealed record Result(Bin Bin, Element Preview, Element Encoded, VideoEncoderChoice Encoder, string Scaler,
        VideoOutputKind PreviewKind = VideoOutputKind.Memory);

    private static string Describe(VideoCodec codec, ScreenShareSettings settings, VideoEncoderChoice encoder,
        string scale, string description = Description) =>
        string.Format(CultureInfo.InvariantCulture, description, settings.Framerate, settings.MaxHeight,
            encoder.ToLaunchString(EncoderName), VideoCodecPipeline.For(codec).Capture + " ! " + EncodedSink, PreviewMaxHeight,
            description == VaDmaBufDescription
                ? $"appsink name={PreviewSinkName} max-buffers=1 drop=true enable-last-sample=false wait-on-eos=false sync=false async=false processing-deadline=0"
                : VideoFramePump.SinkDescription(PreviewSinkName),
            string.Format(CultureInfo.InvariantCulture, scale, settings.MaxHeight),
            (VideoSurfaceSupport.Supports(VideoSurfaceKinds.DmaBuf) && VideoSourceFactory.Nv12DmaBufExportCaps() != null)
                ? "vapostproc ! video/x-raw(memory:DMABuf),format=DMA_DRM ! capsfilter name=preview_caps"
                : "vapostproc ! " + VideoFramePump.FrameCaps);

    // An element that does not take what comes before it raises, so the next description can be tried:
    // an encoder of the list above may still refuse textures, as Media Foundation does without a GPU
    private static Bin? Parse(string description, Action<Exception>? onError)
    {
        try
        {
            return Global.ParseBinFromDescription(description, true);
        }
        catch (Gst.GLib.GException exception)
        {
            onError?.Invoke(exception);
            return null;
        }
    }

    // A preference only applies when it belongs to the negotiated codec's candidates.
    private static VideoEncoderChoice? SelectEncoder(VideoCodec codec, int bitrateKbps, string? preferred = null) =>
        VideoEncoderSelector.Select(codec, bitrateKbps,
            preferred ?? Client.Core.Settings.VideoSettings.Current.Encoder);

    /// <summary>
    /// Whether the branch would take DMA-BUF frames: only the VA-API encoder with its postprocessor does.
    /// </summary>
    public static bool CanTakeDmaBuf(VideoCodec codec) =>
        SelectEncoder(codec, 1000) is { } encoder &&
        encoder.Factory.StartsWith("va", StringComparison.Ordinal) && ElementFactory.Find("vapostproc") != null;

    /// <summary>
    /// Changes the bitrate of the encoder of a running branch, and the bound of its frames with it.
    /// The properties are the ones the encoder gets for that bitrate when it is created; an encoder
    /// that does not take them while it runs keeps its bitrate.
    /// </summary>
    public static void SetBitrate(Bin branch, VideoCodec codec, VideoEncoderChoice encoder, int kbps)
    {
        using var element = branch.GetByName(EncoderName);

        if (element == null || SelectEncoder(codec, kbps, encoder.Factory) is not { } choice || choice.Factory != encoder.Factory)
            return;

        foreach (var (name, value) in choice.Properties.Where(property => VideoEncoderSelector.DependsOnBitrate(property.Name)))
            Global.UtilSetObjectArg(element, name, value);
    }

    /// <summary>
    /// Builds the branch the capture is linked to.
    /// </summary>
    /// <param name="settings">
    /// The height the picture is limited to, the rate it is paced at and the bitrate it is encoded with.
    /// </param>
    /// <param name="isD3D11Source">
    /// The source is a Direct3D 11 screen capture, whose textures can go to the encoder without
    /// passing through memory.
    /// </param>
    /// <param name="isDmaBufSource">
    /// The source may hand out DMA-BUF frames, see <see cref="CanTakeDmaBuf"/>.
    /// </param>
    /// <returns>Null when an element is missing.</returns>
    public static Result? Build(VideoCodec codec, ScreenShareSettings settings, bool isD3D11Source = false,
        bool isDmaBufSource = false, Action<Exception>? onError = null)
    {
        Bin? ParseBranch(string description) => Parse(description, onError);
        var encoder = SelectEncoder(codec, settings.EffectiveBitrateKbps);

        if (encoder == null)
            return null;

        var scale = encoder.Factory.StartsWith("va", StringComparison.Ordinal) && ElementFactory.Find("vapostproc") != null
            ? VaScale
            : CpuScale;
        string scaler;
        Bin? branch = null;
        var previewKind = VideoOutputKind.Memory;

        // Both legs end in their sinks, so the only open end is the input
        if (isD3D11Source && D3D11Encoders.Contains(encoder.Factory) &&
            ElementFactory.Find("d3d11convert") != null && ElementFactory.Find("d3d11download") != null)
        {
            branch = ParseBranch(Describe(codec, settings, encoder, CpuScale, D3D11Description));
            scaler = "GPU (Direct3D 11)";
        }
        else
        {
            scaler = "";
        }

        if (branch == null && isDmaBufSource && scale == VaScale)
        {
            branch = ParseBranch(Describe(codec, settings, encoder, scale, VaDmaBufDescription));
            scaler = "GPU (VA-API, DMA-BUF)";
            if (branch != null && VideoSurfaceSupport.Supports(VideoSurfaceKinds.DmaBuf) &&
                VideoSourceFactory.Nv12DmaBufExportCaps() != null)
                previewKind = VideoOutputKind.DmaBuf;
        }

        if (previewKind == VideoOutputKind.DmaBuf)
        {
            using var filter = branch!.GetByName("preview_caps");
            Global.UtilSetObjectArg(filter!, "caps", VideoSourceFactory.Nv12DmaBufExportCaps()!);
        }

        // A VA surface cannot be handed to the generic CPU branch unchanged.
        if (branch == null && isDmaBufSource)
            return null;

        if (branch == null)
        {
            branch = ParseBranch(Describe(codec, settings, encoder, scale));
            scaler = scale == VaScale ? "GPU (VA-API)" : "CPU";
        }

        // The postprocessor may not take what the capture gives
        if (branch == null && scale != CpuScale)
        {
            scaler = "CPU";
            branch = ParseBranch(Describe(codec, settings, encoder, CpuScale));
        }

        if (branch?.GetByName(PreviewSinkName) is not { } preview)
        {
            branch?.Dispose();
            return null;
        }

        if (branch.GetByName(EncodedSinkName) is not { } encoded)
        {
            preview.Dispose();
            branch.Dispose();
            return null;
        }

        return new Result(branch, preview, encoded, encoder, scaler, previewKind);
    }
}
