using Client.Core.Media;
using Client.Tools.AppInfo;
using Gst;

namespace Client.GStreamer.Video;

/// <summary>
/// Picks the video decoder the same way <see cref="VideoEncoderSelector"/> picks the encoder: by
/// what the registry offers, hardware first and the software one as the last resort.
/// </summary>
public static class VideoDecoderSelector
{
    /// <returns>The factory name of the decoder, or null when the runtime has none for the codec.</returns>
    public static string? Select(VideoCodec codec) =>
        GetCandidates(codec).FirstOrDefault(factory => Decodes(factory, codec));

    /// <summary>
    /// The decoder elements of <paramref name="codec"/> this system can use, in order of preference.
    /// </summary>
    public static IReadOnlyList<string> GetAvailableDecoders(VideoCodec codec) =>
        GetCandidates(codec).Where(factory => Decodes(factory, codec)).ToList();

    // VideoToolbox decodes every codec in one element, which takes only those the chip decodes in
    // hardware
    private static bool Decodes(string factory, VideoCodec codec)
    {
        using var found = ElementFactory.Find(factory);

        if (found == null)
            return false;

        if (!factory.StartsWith("vtdec", StringComparison.Ordinal))
            return true;

        using var caps = Caps.FromString(codec switch
        {
            VideoCodec.H264 => "video/x-h264",
            VideoCodec.AV1 => "video/x-av1",
            VideoCodec.VP8 => "video/x-vp8",
            _ => "video/x-vp9"
        });

        return caps != null && found.CanSinkAnyCaps(caps);
    }

    internal static bool IsKnownDecoder(string factory) =>
        Enum.GetValues<VideoCodec>().SelectMany(GetAllCandidates).Contains(factory);

    // Without hardware acceleration only the software decoders are offered
    private static IEnumerable<string> GetCandidates(VideoCodec codec) =>
        GetAllCandidates(codec).Where(factory => HardwareAcceleration.IsEnabled || VideoElementNames.IsSoftware(factory));

    private static string[] GetAllCandidates(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => OperatingSystem.IsWindows()
            ? ["d3d11h264dec", "nvh264dec", "openh264dec"]
            : OperatingSystem.IsMacOS()
            ? ["vtdec_hw", "openh264dec"]
            : ["vah264dec", "nvh264dec", "openh264dec"],
        VideoCodec.AV1 => OperatingSystem.IsWindows()
            ? ["d3d11av1dec", "nvav1dec", "dav1ddec"]
            : ["vaav1dec", "nvav1dec", "dav1ddec"],
        VideoCodec.VP8 => ["vp8dec"],
        VideoCodec.VP9 => OperatingSystem.IsWindows()
            ? ["d3d11vp9dec", "vp9dec"]
            : ["vavp9dec", "vp9dec"],
        _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, null)
    };
}
