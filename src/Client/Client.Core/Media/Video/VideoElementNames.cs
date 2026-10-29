namespace Client.Core.Media;

/// <summary>
/// The names users know the video encoders and decoders and the ways to the screen by, rather than
/// the names of their GStreamer elements.
/// </summary>
public static class VideoElementNames
{
    private static readonly string[] SoftwareElements =
        ["openh264dec", "openh264enc", "dav1ddec", "svtav1enc", "vp8dec", "vp8enc", "vp9dec", "vp9enc"];

    /// <summary>The CPU does the work of the element, not the GPU.</summary>
    public static bool IsSoftware(string element) => SoftwareElements.Contains(element);

    /// <summary>The technology or the library behind an element, such as "AMD AMF" for amfh264enc.</summary>
    public static string Of(string element) => element switch
    {
        _ when element.StartsWith("d3d11", StringComparison.Ordinal) => "Direct3D 11",
        _ when element.StartsWith("nv", StringComparison.Ordinal) =>
            element.EndsWith("enc", StringComparison.Ordinal) ? "NVIDIA NVENC" : "NVIDIA NVDEC",
        _ when element.StartsWith("va", StringComparison.Ordinal) => "VA-API",
        _ when element.StartsWith("qsv", StringComparison.Ordinal) => "Intel Quick Sync",
        _ when element.StartsWith("amf", StringComparison.Ordinal) => "AMD AMF",
        _ when element.StartsWith("mf", StringComparison.Ordinal) => "Media Foundation",
        _ when element.StartsWith("openh264", StringComparison.Ordinal) => "OpenH264",
        _ when element.StartsWith("dav1d", StringComparison.Ordinal) => "dav1d",
        _ when element.StartsWith("svtav1", StringComparison.Ordinal) => "SVT-AV1",
        _ when element.StartsWith("vp", StringComparison.Ordinal) => "libvpx",
        _ => element
    };

    /// <summary>The GPU kinds among <paramref name="kinds"/>, by name, such as "Direct3D 11".</summary>
    public static IReadOnlyList<string> Of(VideoSurfaceKinds kinds)
    {
        var names = new List<string>();

        if ((kinds & VideoSurfaceKinds.D3D11Shared) != 0)
            names.Add("Direct3D 11");

        if ((kinds & VideoSurfaceKinds.DmaBuf) != 0)
            names.Add("DMA-BUF");

        if ((kinds & VideoSurfaceKinds.CudaGl) != 0)
            names.Add("CUDA");

        return names;
    }
}
