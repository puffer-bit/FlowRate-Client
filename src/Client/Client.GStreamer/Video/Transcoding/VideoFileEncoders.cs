using System.Globalization;
using Gst;
using Client.Tools.AppInfo;

namespace Client.GStreamer.Video.Transcoding;

/// <summary>
/// An encoder element as a gst-launch fragment.
/// </summary>
internal sealed record FileEncoder(string Factory, string LaunchString, bool IsHardware);

/// <summary>
/// The encoders a video file is compressed with. Unlike the ones of screen sharing (see
/// <see cref="VideoEncoderSelector"/>), they are tuned for size at a given quality, not for latency:
/// a variable bitrate, B-frames where the encoder has them, and a keyframe each two seconds so the
/// player can seek.
/// </summary>
internal static class VideoFileEncoders
{
    // Only the properties the element of this system takes are set; the bitrate is always taken
    private sealed record Candidate(string Factory, bool IsHardware, Func<int, int, (string Name, string Value)[]> Required,
        Func<int, (string Name, string Value)[]> Tuning);

    /// <summary>
    /// The H.264 encoders to try in order: the first hardware one this system has, then openh264,
    /// which a hardware encoder that fails on the file falls back to.
    /// </summary>
    public static IReadOnlyList<FileEncoder> H264(int kbps, int keyFrameInterval)
    {
        var available = Candidates()
            .Where(candidate => HardwareAcceleration.IsEnabled || !candidate.IsHardware)
            .Where(candidate => ElementFactory.Find(candidate.Factory) != null)
            .ToList();
        var chosen = available.Where(candidate => candidate.IsHardware).Take(1)
            .Concat(available.Where(candidate => !candidate.IsHardware));

        return chosen.Select(candidate =>
        {
            var properties = candidate.Required(kbps, keyFrameInterval)
                .Concat(candidate.Tuning(keyFrameInterval)
                    .Where(property => VideoEncoderSelector.Accepts(candidate.Factory, property.Name, property.Value)));
            var launch = string.Join(' ', properties.Select(property => $"{property.Name}={property.Value}").Prepend(candidate.Factory));
            return new FileEncoder(candidate.Factory, launch, candidate.IsHardware);
        }).ToList();
    }

    /// <summary>
    /// The audio encoder: AAC where the runtime has one, which every player takes; Opus otherwise.
    /// </summary>
    public static FileEncoder? Audio(int kbps)
    {
        var bitrate = Format(kbps * 1000);
        (string Factory, string Launch)[] candidates =
        [
            ("fdkaacenc", $"fdkaacenc bitrate={bitrate}"),
            ("mfaacenc", $"mfaacenc bitrate={bitrate}"),
            ("atenc", "atenc"),
            ("opusenc", $"opusenc bitrate={bitrate}")
        ];
        return candidates.Where(candidate => ElementFactory.Find(candidate.Factory) != null)
            .Select(candidate => new FileEncoder(candidate.Factory, candidate.Launch, false))
            .FirstOrDefault();
    }

    private static IEnumerable<Candidate> Candidates() => OperatingSystem.IsWindows()
        ? [Nvidia("nvd3d11h264enc"), NvidiaLegacy("nvh264enc"), Qsv("qsvh264enc"), Amf("amfh264enc"),
           MediaFoundation("mfh264enc"), OpenH264]
        : OperatingSystem.IsMacOS()
            ? [VideoToolbox("vtenc_h264_hw"), OpenH264]
            : [Nvidia("nvcudah264enc"), NvidiaLegacy("nvh264enc"), Va("vah264enc"), Va("vah264lpenc"), OpenH264];

    private static Candidate Nvidia(string factory) => new(factory, true,
        (kbps, _) => [("bitrate", Format(kbps))],
        gop => [("rate-control", "vbr"), ("gop-size", Format(gop)), ("preset", "p5"), ("tune", "high-quality")]);

    private static Candidate NvidiaLegacy(string factory) => new(factory, true,
        (kbps, _) => [("bitrate", Format(kbps))],
        gop => [("rc-mode", "vbr"), ("gop-size", Format(gop)), ("preset", "hq")]);

    private static Candidate Va(string factory) => new(factory, true,
        (kbps, _) => [("bitrate", Format(kbps))],
        gop => [("rate-control", "vbr"), ("key-int-max", Format(gop)), ("target-usage", "4")]);

    private static Candidate Qsv(string factory) => new(factory, true,
        (kbps, _) => [("bitrate", Format(kbps))],
        gop => [("rate-control", "vbr"), ("gop-size", Format(gop))]);

    private static Candidate Amf(string factory) => new(factory, true,
        (kbps, _) => [("bitrate", Format(kbps))],
        gop => [("usage", "transcoding"), ("rate-control", "vbr"), ("gop-size", Format(gop))]);

    private static Candidate MediaFoundation(string factory) => new(factory, true,
        (kbps, _) => [("bitrate", Format(kbps))],
        gop => [("rc-mode", "uvbr"), ("gop-size", Format(gop))]);

    private static Candidate VideoToolbox(string factory) => new(factory, true,
        (kbps, _) => [("bitrate", Format(kbps))],
        gop => [("max-keyframe-interval", Format(gop)), ("allow-frame-reordering", "true")]);

    // openh264 takes bit/s and makes the constrained baseline profile only, so it gets the medium
    // complexity, which is close to real time for 1080p on one core
    private static readonly Candidate OpenH264 = new("openh264enc", false,
        (kbps, gop) => [("bitrate", Format(kbps * 1000)), ("gop-size", Format(gop))],
        _ => [("rate-control", "bitrate"), ("usage-type", "camera"), ("complexity", "medium")]);

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
