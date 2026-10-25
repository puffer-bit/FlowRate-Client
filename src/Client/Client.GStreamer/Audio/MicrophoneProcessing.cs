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
    /// The RNNoise element of gst-plugins-rs (rsaudiofx), behind the neural levels and the voice
    /// detection of the gate.
    /// </summary>
    public const string NeuralElementName = "audiornnoise";

    /// <summary>
    /// The format the processing works on. The library takes only a few sample rates.
    /// </summary>
    public const string Caps = "audio/x-raw,rate=48000,channels=1";

    /// <summary>
    /// The only format RNNoise takes: interleaved 32-bit float at 48 kHz, which is what its network
    /// was trained on. The rate and the channels are already those of <see cref="Caps"/>.
    /// </summary>
    public const string NeuralCaps = "audio/x-raw,format=F32LE,layout=interleaved,rate=48000";

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

    // In front of webrtcdsp, so the gain is set on the voice and not raised along with the noise.
    // Its threshold stays at 0: every frame comes out denoised and none is silenced, that is the
    // job of the gate. The converter after it goes back to what webrtcdsp takes.
    private const string Neural =
        "audioconvert ! " + NeuralCaps + " ! " + NeuralElementName + " ! audioconvert ! ";

    /// <summary>
    /// Whether the runtime has RNNoise. A runtime built before it was bundled lacks it.
    /// </summary>
    public static bool HasNeural => ElementFactory.Find(NeuralElementName) != null;

    /// <summary>
    /// The level the processing really runs with: a neural one becomes
    /// <see cref="NoiseSuppressionLevel.Moderate"/> without RNNoise in the runtime.
    /// </summary>
    public static NoiseSuppressionLevel Resolve(NoiseSuppressionLevel noiseSuppression) =>
        IsNeural(noiseSuppression) && !HasNeural ? NoiseSuppressionLevel.Moderate : noiseSuppression;

    /// <summary>
    /// The fragment to put after a caps filter of <see cref="Caps"/>, ending with a link, or nothing
    /// when the runtime has none of the elements: the voice is then sent unprocessed.
    /// </summary>
    /// <remarks>
    /// The elements read their settings only when they start, so another level takes a new fragment.
    /// A level the runtime cannot run is replaced as <see cref="Resolve"/> says; callers that want to
    /// report it resolve the level first.
    /// </remarks>
    public static string Fragment(NoiseSuppressionLevel noiseSuppression)
    {
        noiseSuppression = Resolve(noiseSuppression);

        var neural = IsNeural(noiseSuppression) ? Neural : "";

        if (ElementFactory.Find(ElementName) == null)
            return neural;

        return neural + string.Format(Cleanup, DescribeNoiseSuppression(noiseSuppression)) + Gain;
    }

    private static bool IsNeural(NoiseSuppressionLevel level) =>
        level is NoiseSuppressionLevel.Neural or NoiseSuppressionLevel.NeuralAndWebRtc;

    private static string DescribeNoiseSuppression(NoiseSuppressionLevel level) => level switch
    {
        // RNNoise has done it already
        NoiseSuppressionLevel.Off or NoiseSuppressionLevel.Neural => "noise-suppression=false",
        NoiseSuppressionLevel.Low => "noise-suppression=true noise-suppression-level=low",
        NoiseSuppressionLevel.High => "noise-suppression=true noise-suppression-level=high",
        NoiseSuppressionLevel.VeryHigh => "noise-suppression=true noise-suppression-level=very-high",
        _ => "noise-suppression=true noise-suppression-level=moderate"
    };
}
