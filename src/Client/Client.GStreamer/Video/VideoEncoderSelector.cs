using System.Globalization;
using Gst;

namespace Client.GStreamer.Video;

public enum VideoCodec
{
    H264,
    AV1,
    VP8,
    VP9
}

/// <summary>
/// An encoder element and the properties that tune it for low-latency screen sharing.
/// </summary>
public sealed record VideoEncoderChoice(string Factory, IReadOnlyList<(string Name, string Value)> Properties, bool IsHardware)
{
    /// <summary>
    /// The element as a gst-launch fragment, e.g. "openh264enc bitrate=4000000 usage-type=screen".
    /// </summary>
    public string ToLaunchString() =>
        Properties.Count == 0 ? Factory : $"{Factory} {string.Join(' ', Properties.Select(p => $"{p.Name}={p.Value}"))}";

    public Element? CreateElement(string? name)
    {
        var element = ElementFactory.Make(Factory, name);
        if (element == null)
            return null;

        // Converts the string like gst-launch does, so enum nicks work too
        foreach (var (propertyName, value) in Properties)
            Global.UtilSetObjectArg(element, propertyName, value);

        return element;
    }
}

/// <summary>
/// Picks the video encoder by what the GStreamer registry really offers instead of by the GPU vendor:
/// the hardware plugins (nvcodec, va, qsv, amfcodec, mediafoundation) register their elements only when
/// the device and driver support them. Hardware encoders come first, the software one is the last
/// resort. Every candidate is free of GPL code.
/// </summary>
public static class VideoEncoderSelector
{
    private sealed record Candidate(string Factory, bool IsHardware, Func<int, (string, string)[]> Properties);

    /// <param name="preferred">The factory to use when this system has it, before the usual order.</param>
    public static VideoEncoderChoice? Select(VideoCodec codec, int bitrateKbps, string? preferred = null)
    {
        var candidates = GetCandidates(codec).ToList();
        var first = candidates.FindIndex(candidate => candidate.Factory == preferred);

        if (first > 0)
        {
            candidates.Insert(0, candidates[first]);
            candidates.RemoveAt(first + 1);
        }

        foreach (var candidate in candidates)
        {
            if (ElementFactory.Find(candidate.Factory) == null)
                continue;

            return new VideoEncoderChoice(candidate.Factory, candidate.Properties(bitrateKbps), candidate.IsHardware);
        }

        return null;
    }

    /// <summary>
    /// The encoder elements of <paramref name="codec"/> this system can use, in order of preference.
    /// </summary>
    public static IReadOnlyList<string> GetAvailableEncoders(VideoCodec codec) =>
        GetCandidates(codec).Where(c => ElementFactory.Find(c.Factory) != null).Select(c => c.Factory).ToList();

    /// <summary>
    /// Whether a hardware encoder for <paramref name="codec"/> is available, i.e. whether the device and its
    /// driver registered one. Without it <see cref="Select"/> falls back to software encoding.
    /// </summary>
    public static bool HasHardwareEncoder(VideoCodec codec) =>
        GetCandidates(codec).Any(c => c.IsHardware && ElementFactory.Find(c.Factory) != null);

    /// <summary>
    /// All encoder elements considered for <paramref name="codec"/> on this system.
    /// </summary>
    public static IReadOnlyList<string> GetKnownEncoders(VideoCodec codec, bool hardware) =>
        GetCandidates(codec).Where(c => c.IsHardware == hardware).Select(c => c.Factory).ToList();

    /// <summary>
    /// The parser that follows the encoder, or null when the codec needs none.
    /// </summary>
    public static string? GetParser(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "h264parse",
        VideoCodec.AV1 => "av1parse",
        _ => null
    };

    public static string GetEncodedCaps(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "video/x-h264,stream-format=byte-stream,alignment=au",
        VideoCodec.AV1 => "video/x-av1,stream-format=obu-stream,alignment=tu",
        VideoCodec.VP8 => "video/x-vp8",
        VideoCodec.VP9 => "video/x-vp9",
        _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, null)
    };

    private static IEnumerable<Candidate> GetCandidates(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => OperatingSystem.IsWindows()
            ? [Hardware("nvd3d11h264enc"), NvidiaLegacy("nvh264enc"), Qsv("qsvh264enc"), Hardware("amfh264enc"),
               MediaFoundation("mfh264enc"), OpenH264]
            : [Hardware("nvcudah264enc"), NvidiaLegacy("nvh264enc"), Va("vah264enc"), Va("vah264lpenc"), OpenH264],
        VideoCodec.AV1 => OperatingSystem.IsWindows()
            ? [Hardware("nvd3d11av1enc"), Qsv("qsvav1enc"), Hardware("amfav1enc"), SvtAv1]
            : [Hardware("nvcudaav1enc"), Hardware("nvav1enc"), Va("vaav1enc"), Va("vaav1lpenc"), SvtAv1],
        VideoCodec.VP8 => [Vpx("vp8enc")],
        VideoCodec.VP9 => OperatingSystem.IsWindows()
            ? [Qsv("qsvvp9enc"), Vpx("vp9enc")]
            : [Va("vavp9enc"), Va("vavp9lpenc"), Vpx("vp9enc")],
        _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, null)
    };

    // Hardware encoders take the bitrate in kbit/s
    private static Candidate Hardware(string factory) =>
        new(factory, true, kbps => [("bitrate", Format(kbps))]);

    private static Candidate NvidiaLegacy(string factory) =>
        new(factory, true, kbps => [("bitrate", Format(kbps)), ("preset", "low-latency-hq"), ("zerolatency", "true")]);

    private static Candidate Va(string factory) =>
        new(factory, true, kbps => [("bitrate", Format(kbps)), ("rate-control", "cbr")]);

    private static Candidate Qsv(string factory) =>
        new(factory, true, kbps => [("bitrate", Format(kbps)), ("rate-control", "cbr")]);

    private static Candidate MediaFoundation(string factory) =>
        new(factory, true, kbps => [("bitrate", Format(kbps)), ("low-latency", "true")]);

    // openh264 takes bit/s and has a mode tuned for screen content
    private static readonly Candidate OpenH264 = new("openh264enc", false, kbps =>
        [("bitrate", Format(kbps * 1000)), ("usage-type", "screen"), ("rate-control", "bitrate"), ("complexity", "low")]);

    // Presets 10 and above are the real-time ones
    private static readonly Candidate SvtAv1 = new("svtav1enc", false, kbps =>
        [("target-bitrate", Format(kbps)), ("preset", "12")]);

    // libvpx takes bit/s; deadline=1 is its real-time mode
    private static Candidate Vpx(string factory) => new(factory, false, kbps =>
        [("target-bitrate", Format(kbps * 1000)), ("deadline", "1"), ("cpu-used", "8"), ("end-usage", "cbr")]);

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
