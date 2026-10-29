using Client.Core.Media;
using Client.Core.Settings;
using Gst;

namespace Client.GStreamer.Video;

/// <summary>
/// The form decoded frames leave a decoding branch in.
/// </summary>
internal enum VideoOutputKind
{
    /// <summary>BGRA pixels in memory, shown by copying.</summary>
    Memory,

    /// <summary>NV12 or I420 planes in memory, uploaded by an OpenGL presenter.</summary>
    MemoryYuv,

    /// <summary>DMA-BUF descriptors of a VA-API decoder.</summary>
    DmaBuf,

    /// <summary>CUDA memory of an NVIDIA decoder.</summary>
    Cuda,

    /// <summary>Direct3D 11 textures.</summary>
    D3D11
}

/// <summary>
/// A decoder and what follows it up to the appsink, as a gst-launch fragment.
/// </summary>
internal sealed record VideoOutput(string Decoder, VideoOutputKind Kind, string Chain);

/// <summary>
/// Picks how the frames of a stream are decoded and in what form they are delivered, from the
/// video settings, what the user interface can show without copying and what the registry offers.
/// </summary>
/// <remarks>
/// Whatever cannot be used falls back to the plain path: the decoder hands its frames over in
/// memory, as NV12 or I420 when an OpenGL presenter uploads the planes (videoconvert passes them
/// through untouched), or turned into BGRA by videoconvert for the bitmap. Converting on
/// the GPU first and downloading BGRA is not faster: on AMD with VA-API, reading BGRA back from
/// vapostproc gave 28 frames per second at 1080p, the plain path 144.
/// </remarks>
internal static class VideoOutputSelector
{
    private const string MemoryCaps = "video/x-raw,format=BGRA";

    // What decoders output anyway; anything else (10-bit, 4:4:4) is converted by videoconvert
    private const string YuvCaps = "video/x-raw,format=(string){NV12,I420}";

    /// <param name="allowGpuSurfaces">False to deliver frames in memory whatever is supported.</param>
    /// <returns>Null when the system has no decoder for the codec.</returns>
    public static VideoOutput? Select(VideoCodec codec, bool allowGpuSurfaces = true)
    {
        var settings = VideoSettings.Current;
        var decoders = VideoDecoderSelector.GetAvailableDecoders(codec);

        if (decoders.Count == 0)
            return null;

        string decoder;

        if (!settings.IsHardwareDecodingEnabled)
            decoder = decoders.FirstOrDefault(IsSoftware) ?? decoders[0];
        else if (settings.Decoder != null && decoders.Contains(settings.Decoder))
            decoder = settings.Decoder;
        else
            decoder = decoders[0];

        if (IsSoftware(decoder))
            return Plain(decoder);

        var surfaces = allowGpuSurfaces && settings.IsZeroCopyEnabled ? VideoSurfaceSupport.Kinds : VideoSurfaceKinds.None;

        if (decoder.StartsWith("va", StringComparison.Ordinal))
            return ForVa(decoder, surfaces);

        if (decoder.StartsWith("d3d11", StringComparison.Ordinal))
            return ForD3D11(decoder, surfaces, null);

        if (decoder.StartsWith("nv", StringComparison.Ordinal))
            return OperatingSystem.IsWindows()
                ? ForD3D11(decoder, surfaces, "video/x-raw(memory:D3D11Memory)")
                : ForCuda(decoder, surfaces);

        return Plain(decoder);
    }

    private static VideoOutput ForVa(string decoder, VideoSurfaceKinds surfaces) =>
        (surfaces & VideoSurfaceKinds.DmaBuf) != 0
            ? new VideoOutput(decoder, VideoOutputKind.DmaBuf, $"{decoder} ! video/x-raw(memory:DMABuf),format=DMA_DRM")
            : Plain(decoder);

    private static VideoOutput ForCuda(string decoder, VideoSurfaceKinds surfaces) =>
        (surfaces & VideoSurfaceKinds.CudaGl) != 0
            ? new VideoOutput(decoder, VideoOutputKind.Cuda, $"{decoder} ! video/x-raw(memory:CUDAMemory),format=NV12")
            : Plain(decoder);

    /// <param name="decoderCaps">What the decoder has to output for the converter to stay on the GPU, if anything.</param>
    private static VideoOutput ForD3D11(string decoder, VideoSurfaceKinds surfaces, string? decoderCaps)
    {
        if (!Has("d3d11convert"))
            return Plain(decoder);

        var head = decoderCaps == null ? decoder : $"{decoder} ! {decoderCaps}";

        // Without a shared texture the planes as decoded are downloaded for the screen to upload: NV12
        // is under half of BGRA, and a screen of 144 fps fell behind on the download of BGRA
        if ((surfaces & VideoSurfaceKinds.D3D11Shared) == 0 && VideoSurfaceSupport.Supports(VideoSurfaceKinds.YuvMemory) &&
            Has("d3d11download"))
            return new VideoOutput(decoder, VideoOutputKind.MemoryYuv, $"{head} ! d3d11download ! {YuvCaps}");

        // Otherwise the GPU still converts to BGRA, so only the download is left to the CPU
        if ((surfaces & VideoSurfaceKinds.D3D11Shared) == 0)
            return Has("d3d11download")
                ? new VideoOutput(decoder, VideoOutputKind.Memory,
                    $"{head} ! d3d11convert ! video/x-raw(memory:D3D11Memory),format=BGRA ! d3d11download ! {MemoryCaps}")
                : Plain(decoder);

        return new VideoOutput(decoder, VideoOutputKind.D3D11,
            $"{head} ! d3d11convert ! video/x-raw(memory:D3D11Memory),format=BGRA");
    }

    /// <summary>
    /// Frames in memory: the YUV planes as decoded when the screen uploads them, otherwise
    /// converted to BGRA by the CPU and copied, the path frames always took.
    /// </summary>
    public static VideoOutput Plain(string decoder) =>
        VideoSurfaceSupport.Supports(VideoSurfaceKinds.YuvMemory)
            ? new VideoOutput(decoder, VideoOutputKind.MemoryYuv, $"{decoder} ! videoconvert ! {YuvCaps}")
            : new VideoOutput(decoder, VideoOutputKind.Memory, $"{decoder} ! videoconvert ! {MemoryCaps}");

    private static bool IsSoftware(string decoder) => VideoElementNames.IsSoftware(decoder);

    private static bool Has(string factory) => ElementFactory.Find(factory) != null;
}
