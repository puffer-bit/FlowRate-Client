using Client.Core.Settings;
using Gst;

namespace Client.GStreamer.Audio;

/// <summary>
/// What a microphone goes through before the input gain: a high-pass filter, noise suppression and
/// automatic gain, which brings a quiet microphone up to the level speech is expected at. Without
/// it the voice is exactly as loud as the device delivers it, which is far below what other
/// applications send.
/// </summary>
internal static class MicrophoneProcessing
{
    private const string ElementName = "webrtcdsp";

    /// <summary>
    /// The format the processing works on. The library takes only a few sample rates.
    /// </summary>
    public const string Caps = "audio/x-raw,rate=48000,channels=1";

    // No echo cancelling: it needs the played sound from a webrtcechoprobe, and the element refuses
    // to start without one.
    private const string Cleanup =
        ElementName + " echo-cancel=false high-pass-filter=true {0} gain-control=true limiter=true ! ";

    // The element does not pass its gain mode on to the library, so whatever compression-gain-db
    // says, one instance raises quiet speech by about 10 dB and no more. A second one takes speech
    // from -40 dBFS up to about -18 dBFS, where the first leaves it at -28; loud speech passes both
    // nearly unchanged, and the limiter of each keeps the peaks below full scale.
    private const string Gain =
        ElementName + " echo-cancel=false high-pass-filter=false noise-suppression=false " +
        "gain-control=true limiter=true ! ";

    /// <summary>
    /// The fragment to put after a caps filter of <see cref="Caps"/>, ending with a link, or nothing
    /// when the runtime has no such element: the voice is then sent unprocessed.
    /// </summary>
    /// <remarks>
    /// The element reads its settings only when it starts, so another level takes a new fragment.
    /// </remarks>
    public static string Fragment(NoiseSuppressionLevel noiseSuppression) =>
        ElementFactory.Find(ElementName) != null
            ? string.Format(Cleanup, DescribeNoiseSuppression(noiseSuppression)) + Gain
            : "";

    private static string DescribeNoiseSuppression(NoiseSuppressionLevel level) => level switch
    {
        NoiseSuppressionLevel.Off => "noise-suppression=false",
        NoiseSuppressionLevel.Low => "noise-suppression=true noise-suppression-level=low",
        NoiseSuppressionLevel.High => "noise-suppression=true noise-suppression-level=high",
        NoiseSuppressionLevel.VeryHigh => "noise-suppression=true noise-suppression-level=very-high",
        _ => "noise-suppression=true noise-suppression-level=moderate"
    };
}
