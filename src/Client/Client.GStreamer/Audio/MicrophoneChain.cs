using System.Globalization;
using Gst;
using Client.Core.Settings;
using Client.GStreamer.Interop;

namespace Client.GStreamer.Audio;

/// <summary>
/// What a captured sound goes through before it is encoded: the capture with its processing, the input
/// gain, and for a microphone the gate with its voice detection. A transport puts its encoder behind it;
/// the microphone test plays it back, so the user hears what the others would.
/// </summary>
/// <remarks>
/// A chain is a bin of two parts: the capture part, named <see cref="CaptureName"/>, and the part after
/// it, named <see cref="TailName"/>. The capture part is the only one that depends on the device and on
/// the noise suppression, so it is replaced when either changes, see <see cref="ReplaceCapture"/>.
/// </remarks>
public static class MicrophoneChain
{
    /// <summary>
    /// Name of the volume element that applies the input gain.
    /// </summary>
    public const string InputVolumeName = "input_volume";

    /// <summary>
    /// Name of the level element that measures the input for the <see cref="VoiceGate"/>. It is
    /// after the input volume, so the gate hears what would be sent.
    /// </summary>
    public const string GateLevelName = "gate_level";

    /// <summary>
    /// Name of the element the <see cref="VoiceGate"/> unmutes while the user speaks.
    /// </summary>
    public const string GateValveName = "gate";

    /// <summary>
    /// Name of the level element that measures what RNNoise takes for a voice, for the voice
    /// detection of the <see cref="VoiceGate"/>. Only a runtime with RNNoise has it.
    /// </summary>
    public const string VoiceLevelName = "voice_level";

    /// <summary>
    /// Name of the valve in front of RNNoise in the voice detection, closed while the gate is off.
    /// </summary>
    public const string VoiceValveName = "voice_valve";

    /// <summary>
    /// Name of the part that captures: the source and what its sound goes through before the input gain.
    /// </summary>
    private const string CaptureName = "capture";

    /// <summary>
    /// Name of the part that stays for the whole life of a chain, which a new capture part is linked to.
    /// A bin that runs a chain gives this name to what follows the capture.
    /// </summary>
    public const string TailName = "tail";

    /// <summary>
    /// Name of the queue that holds the audio back by <see cref="VoiceGate.PreRoll"/> between the
    /// level element and the valve of the gate.
    /// </summary>
    private const string GateDelayName = "gate_delay";

    // Where the voice detection leaves the gate, in front of the delay
    private const string GateSplitName = "gate_split";

    // The caps filter is spelled out: the short form of one cannot end a description, which it
    // does for a source that is not processed. A queue of a live source never drains once filled,
    // so a stall would stay in the voice as latency for good; the short one makes the source drop
    // the excess instead, and its timestamps go on over what it dropped.
    private const string CaptureDescription =
        "queue max-size-buffers=0 max-size-bytes=0 max-size-time=100000000 ! audioconvert ! audioresample ! capsfilter caps=\"{1}\" ! {0}";

    // The sound of a screen is music and films as often as speech, so it keeps both channels
    private const string ScreenCaps = "audio/x-raw,rate=48000,channels=2";

    // The voice detection, beside the delay. RNNoise silences every 10 ms frame it takes for no voice,
    // and the level after it tells the gate what is left; nothing of it is sent. Its queue is leaky,
    // so a detector that falls behind never holds up the voice. The valve keeps RNNoise idle while
    // the gate is off and passes the caps meanwhile, so it starts at once when turned on.
    // While the valve drops, tee fails the allocation query on its way, and the elements in front
    // of it allocate their buffers on their own, as they do without a downstream pool.
    private const string VoiceDetectionDescription =
        " " + GateSplitName + ". ! queue leaky=downstream max-size-buffers=0 max-size-bytes=0 max-size-time=100000000 ! " +
        "valve name=" + VoiceValveName + " drop=true drop-mode=forward-sticky-events ! audioconvert ! " +
        MicrophoneProcessing.NeuralCaps + " ! " + MicrophoneProcessing.NeuralElementName + " voice-activity-threshold={1} ! " +
        "level name=" + VoiceLevelName + " interval={0} ! fakesink sync=false async=false";

    // What a chain of a voice has in front of the encoder, the meter split off after the gain.
    // The trailing identity gives the bin its src pad once the gate is left out.
    private const string MonitorDescription =
        "volume name=" + InputVolumeName + " volume={0} ! tee name=" + MonitorSplitName + " ! {1}identity" +
        " {2} " + MonitorSplitName + ". ! {3}";

    private const string MonitorSplitName = "monitor_split";

    // Starts closed, that is silent. The queue lets nothing out until it holds the pre-roll, so the
    // valve gets the audio that much later than level measures it. It adds the pre-roll to the
    // latency it reports, lets out all it holds at the end of the stream and empties on a flush.
    // Its upper limit only guards against a stalled valve: the queue behind the encoder is leaky,
    // so it never fills.
    private const string GateDescription =
        "level name=" + GateLevelName + " interval={0} ! {2}" +
        "queue name=" + GateDelayName + " max-size-buffers=0 max-size-bytes=0 max-size-time=200000000 " +
        "min-threshold-time={1} ! " +
        "volume name=" + GateValveName + " mute=true ! ";

    /// <summary>
    /// The gate of a microphone and the voice detection beside it, as fragments of a launch description,
    /// empty when the chain has none. The gate goes in line after the input gain and ends with a link;
    /// the voice detection is a separate branch, appended to the description.
    /// </summary>
    /// <param name="isMicrophone">
    /// Whether the chain is the one of a microphone. Its gate is closed until a <see cref="VoiceGate"/>
    /// opens it, and stays even while it is turned off, so it can be turned on again without rebuilding.
    /// Each is left out when the runtime lacks its element.
    /// </param>
    public static (string Gate, string VoiceDetection) DescribeGate(bool isMicrophone)
    {
        // A runtime built before the gate has no level element; the voice still goes out, just ungated
        if (!isMicrophone || ElementFactory.Find("level") == null)
            return ("", "");

        // Built along with the gate even while voice detection is off, so it can be turned on in a call
        var hasVoiceDetection = MicrophoneProcessing.HasNeural;
        var interval = (ulong)VoiceGate.LevelInterval.TotalNanoseconds;

        // The delay is fixed with the chain: changed while the voice flows, it would leave a hole
        // or a burst in it
        var gate = string.Format(CultureInfo.InvariantCulture, GateDescription, interval,
            (ulong)(hasVoiceDetection ? VoiceGate.PreRoll + VoiceGate.VoiceDetectionDelay : VoiceGate.PreRoll)
            .TotalNanoseconds,
            hasVoiceDetection ? "tee name=" + GateSplitName + " ! " : "");

        var voiceDetection = hasVoiceDetection
            ? string.Format(CultureInfo.InvariantCulture, VoiceDetectionDescription, interval,
                VoiceGate.VoiceActivityThreshold)
            : "";

        return (gate, voiceDetection);
    }

    /// <summary>
    /// Builds the chain of a microphone around <paramref name="source"/>, which it takes ownership of,
    /// ending in raw audio: the capture with its processing, the input gain and the gate with its voice
    /// detection.
    /// </summary>
    /// <param name="meter">
    /// A description fed with the sound in front of the gate, as the gate measures it, ending in a sink.
    /// </param>
    /// <returns>A bin with a single src pad carrying raw audio, or null when an element is missing.</returns>
    public static Bin? BuildMonitor(Element source, double volume, NoiseSuppressionLevel noiseSuppression,
        string? echoProbe, string meter)
    {
        var (gate, voiceDetection) = DescribeGate(true);

        var processing = Global.ParseBinFromDescription(string.Format(CultureInfo.InvariantCulture,
            MonitorDescription, volume, gate, voiceDetection, meter), true);

        if (processing == null)
        {
            source.Dispose();
            return null;
        }

        processing.SetProperty("name", TailName);

        var branch = Bin.New("microphone_monitor");
        branch.Add(processing);

        using (var output = processing.GetStaticPad("src"))
        using (var ghost = output == null ? null : GhostPad.New("src", output))
        {
            if (ghost == null || !branch.AddPad(ghost))
            {
                processing.Dispose();
                source.Dispose();
                branch.Dispose();
                return null;
            }
        }

        processing.Dispose();

        if (!ReplaceCapture(branch, source, true, false, noiseSuppression, echoProbe))
        {
            branch.Dispose();
            return null;
        }

        return branch;
    }

    /// <summary>
    /// Puts a capture part around <paramref name="source"/>, which it takes ownership of, in front
    /// of the <see cref="TailName"/> part of <paramref name="branch"/>, in place of the one it has.
    /// The branch may be running.
    /// </summary>
    /// <remarks>
    /// The previous part is stopped before it is taken out, and with it the thread that pushed into
    /// the rest of the branch, so nothing flows there in between and no pad has to be blocked.
    /// </remarks>
    /// <param name="isMicrophone">The sound is a voice, which gets the <see cref="MicrophoneProcessing"/>.</param>
    /// <param name="isStereo">The capture keeps two channels, as the sound of a screen does.</param>
    /// <param name="noiseSuppression">How much background noise is taken out of a microphone.</param>
    /// <param name="echoProbe">The echo probe a microphone cancels the played voices with, if any.</param>
    /// <returns>False when the part could not be built or linked; the branch is then left without one.</returns>
    public static bool ReplaceCapture(Bin branch, Element source, bool isMicrophone, bool isStereo,
        NoiseSuppressionLevel noiseSuppression, string? echoProbe)
    {
        var capture = BuildCapture(source, isMicrophone, isStereo, noiseSuppression, echoProbe);

        if (capture == null)
            return false;

        using var tail = branch.GetByName(TailName);

        if (tail == null)
        {
            capture.Dispose();
            return false;
        }

        using (var previous = branch.GetByName(CaptureName))
        {
            if (previous != null)
            {
                GstBranch.Shutdown(previous);
                branch.Remove(previous);
            }
        }

        branch.Add(capture);

        if (!capture.Link(tail))
        {
            branch.Remove(capture);
            capture.Dispose();
            return false;
        }

        capture.SyncStateWithParent();
        capture.Dispose();

        return true;
    }

    private static Bin? BuildCapture(Element source, bool isMicrophone, bool isStereo,
        NoiseSuppressionLevel noiseSuppression, string? echoProbe)
    {
        // The fragment ends with a link, and without it the description would
        var description = string.Format(CaptureDescription,
            isMicrophone ? MicrophoneProcessing.Fragment(noiseSuppression, echoProbe) : "",
            isStereo ? ScreenCaps : MicrophoneProcessing.Caps).TrimEnd(' ', '!');

        var processing = Global.ParseBinFromDescription(description, true);

        if (processing == null)
        {
            source.Dispose();
            return null;
        }

        var capture = Bin.New(CaptureName);
        capture.Add(source);
        capture.Add(processing);

        if (!source.Link(processing))
        {
            processing.Dispose();
            source.Dispose();
            capture.Dispose();
            return null;
        }

        using (var output = processing.GetStaticPad("src"))
        using (var ghost = output == null ? null : GhostPad.New("src", output))
        {
            if (ghost == null || !capture.AddPad(ghost))
            {
                processing.Dispose();
                source.Dispose();
                capture.Dispose();
                return null;
            }
        }

        processing.Dispose();
        source.Dispose();

        return capture;
    }
}
