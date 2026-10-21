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
        GetCandidates(codec).FirstOrDefault(factory => ElementFactory.Find(factory) != null);

    /// <summary>
    /// The decoder elements of <paramref name="codec"/> this system can use, in order of preference.
    /// </summary>
    public static IReadOnlyList<string> GetAvailableDecoders(VideoCodec codec) =>
        GetCandidates(codec).Where(factory => ElementFactory.Find(factory) != null).ToList();

    private static string[] GetCandidates(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => OperatingSystem.IsWindows()
            ? ["d3d11h264dec", "nvh264dec", "openh264dec"]
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
