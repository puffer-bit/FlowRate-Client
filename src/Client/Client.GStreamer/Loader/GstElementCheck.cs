using Gst;
using Microsoft.Extensions.Logging;
using Client.GStreamer.Video;

namespace Client.GStreamer.Loader;

/// <summary>
/// Checks right after initialization that the runtime has every element the application builds its
/// pipelines from, so a broken bundle is reported by name instead of failing deep inside a pipeline.
/// </summary>
internal static partial class GstElementCheck
{
    private static readonly string[] Required =
    [
        // Transport
        "webrtcbin", "rtpbin", "nicesrc", "nicesink", "dtlssrtpenc", "dtlssrtpdec", "sctpenc", "sctpdec",
        // Voice
        "opusenc", "opusdec", "rtpopuspay", "rtpopusdepay",
        "audioconvert", "audioresample", "audiomixer", "audiodynamic", "volume", "valve", "autoaudiosrc", "autoaudiosink",
        // Generic
        "queue", "tee", "capsfilter", "appsrc", "appsink", "fakesink",
        // Video
        "videoconvert", "videoscale", "videorate",
        "h264parse", "openh264enc", "openh264dec", "rtph264pay", "rtph264depay",
        "vp8enc", "vp8dec", "rtpvp8pay", "rtpvp8depay"
    ];

    // The bundle always has them; an installed runtime may lack the gst-plugins-rs part, and AV1 is then not offered
    private static readonly string[] Av1 = ["av1parse", "svtav1enc", "dav1ddec", "rtpav1pay", "rtpav1depay"];

    private static readonly string[] RequiredWindows = ["d3d11screencapturesrc"];

    private static readonly string[] Optional =
    [
        "vp9enc", "vp9dec", "rtpvp9pay", "rtpvp9depay",
        // Required by the build scripts, but a runtime built before the voice gate lacks it, and the
        // microphone is then sent ungated
        "level",
        // Without it the microphone is sent as the device delivers it: no automatic gain, no noise
        // suppression
        "webrtcdsp",
        // RNNoise, bundled since the neural noise suppression. Without it the neural levels run as
        // the moderate one of webrtcdsp
        "audiornnoise"
    ];

    // Missing only without PipeWire on the system, then screen sharing on Wayland is impossible anyway
    private static readonly string[] OptionalLinux = ["pipewiresrc"];

    /// <summary>
    /// Logs what is missing and what encoders and decoders are available.
    /// </summary>
    /// <returns>False when a required element is missing.</returns>
    /// <param name="isBundled">The runtime is the shipped one, not one installed on the system.</param>
    public static bool Run(ILogger logger, bool isBundled)
    {
        var required = OperatingSystem.IsWindows() ? Required.Concat(RequiredWindows) : Required;
        var optional = OperatingSystem.IsLinux() ? Optional.Concat(OptionalLinux) : Optional;
        if (isBundled)
            required = required.Concat(Av1);
        else
            optional = optional.Concat(Av1);

        var missingRequired = FindMissing(required);
        var missingOptional = FindMissing(optional);

        if (missingOptional.Count > 0)
            LogOptionalMissing(logger, string.Join(", ", missingOptional));

        foreach (var codec in Enum.GetValues<VideoCodec>())
        {
            var available = VideoEncoderSelector.GetAvailableEncoders(codec);
            LogEncoders(logger, codec, available.Count > 0 ? string.Join(", ", available) : "none");

            var decoders = VideoDecoderSelector.GetAvailableDecoders(codec);
            LogDecoders(logger, codec, decoders.Count > 0 ? string.Join(", ", decoders) : "none");
        }

        if (missingRequired.Count == 0)
            return true;

        LogRequiredMissing(logger, string.Join(", ", missingRequired));
        return false;
    }

    private static List<string> FindMissing(IEnumerable<string> elements) =>
        elements.Where(name => ElementFactory.Find(name) == null).ToList();
}
