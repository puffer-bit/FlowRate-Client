using System.Globalization;
using Gst;
using Shared.Models.Media;
using Client.Core.Sessions.Hybrid;
using Client.Core.Settings;
using Client.GStreamer.Audio;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// Capture branch of an outbound audio track: the microphone, or the sound of a shown screen,
/// encoded to Opus and packetized with exactly the payload type and SSRC the SFU created the
/// producer with. mediasoup matches the incoming RTP by that SSRC, so any other value would be
/// dropped on arrival.
/// </summary>
internal static class OutgoingAudioBranch
{
    private const int DefaultOpusPayloadType = 100;

    /// <summary>
    /// Name of the element that mutes the microphone. It silences the raw audio before the encoder,
    /// which then sends only the tiny DTX frames, with timestamps that keep running.
    /// </summary>
    public const string MuteValveName = "mute";

    /// <summary>
    /// Name of the volume element in front of the <see cref="MuteValveName"/> valve. It silences the
    /// audio a few frames before the valve closes and until just after it opens, so the encoder
    /// starts and ends on silence instead of being cut in the middle of a sound, which the listeners
    /// hear as a crackle.
    /// </summary>
    public const string MuteFadeName = "mute_fade";

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
    /// Name of the part that captures: the source and what its sound goes through before the input
    /// gain. It is the only part that depends on the device and on the noise suppression, so it is
    /// replaced when either changes, see <see cref="ReplaceCapture"/>.
    /// </summary>
    private const string CaptureName = "capture";

    /// <summary>
    /// Name of the part that stays for the whole life of the track: gain, gate, mute, encoder and
    /// payloader. The RTP sequence and the timestamps go on across a replaced capture part.
    /// </summary>
    private const string EncodingName = "encoding";

    // The caps filter is spelled out: the short form of one cannot end a description, which it
    // does for a source that is not processed. A queue of a live source never drains once filled,
    // so a stall would stay in the voice as latency for good; the short one makes the source drop
    // the excess instead, and its timestamps go on over what it dropped.
    private const string CaptureDescription =
        "queue max-size-buffers=0 max-size-bytes=0 max-size-time=100000000 ! audioconvert ! audioresample ! capsfilter caps=\"{1}\" ! {0}";

    // The sound of a screen is music and films as often as speech, so it keeps both channels
    private const string ScreenCaps = "audio/x-raw,rate=48000,channels=2";

    // Mono speech at up to 64 kbit/s, less when the signal needs less. FEC lets the receiver rebuild
    // a lost packet from the next one, but the encoder only adds the redundancy for the loss it is
    // told to expect, and with the default of 0 it adds none. DTX stops sending while the speaker
    // is silent: the encoder only shrinks silent frames, the payloader's own dtx drops them. The
    // gate and the mute silence the voice instead of dropping it: a hole in the stream would be
    // closed up by the encoder, the RTP clock would not advance over the pause, and the receiver
    // would replay the voice. The last queue drops the oldest packets past 200 ms rather than
    // sending them late.
    private const string EncodingDescription =
        "volume name=" + InputVolumeName + " volume={0} ! {3}" +
        "volume name=" + MuteFadeName + " ! " +
        "volume name=" + MuteValveName + " mute=false ! " +
        "opusenc audio-type=voice bitrate=64000 inband-fec=true packet-loss-percentage=10 dtx=true " +
        "frame-size=20 ! " +
        "rtpopuspay pt={1} ssrc={2} dtx=true ! " +
        "queue name=" + PipelineBranch.SendQueueName + " max-size-buffers=0 max-size-bytes=0 max-size-time=200000000 leaky=downstream";

    // Stereo at a rate that keeps music clean. No DTX: a quiet passage of a film is not a pause,
    // and the producer of the screen sound is created without it. The volume and mute elements
    // are the same as the voice has, so the track is controlled the same way.
    private const string ScreenEncodingDescription =
        "volume name=" + InputVolumeName + " volume={0} ! " +
        "volume name=" + MuteFadeName + " ! " +
        "volume name=" + MuteValveName + " mute=false ! " +
        "opusenc audio-type=generic bitrate=128000 inband-fec=true packet-loss-percentage=10 frame-size=20 ! " +
        "rtpopuspay pt={1} ssrc={2} ! " +
        "queue name=" + PipelineBranch.SendQueueName + " max-size-buffers=0 max-size-bytes=0 max-size-time=200000000 leaky=downstream";

    // Starts closed, that is silent.
    private const string GateDescription =
        "level name=" + GateLevelName + " interval={0} ! " +
        "volume name=" + GateValveName + " mute=true ! ";

    /// <summary>
    /// Builds the branch around <paramref name="source"/>, which it takes ownership of.
    /// </summary>
    /// <param name="isMicrophone">
    /// Whether the source is a microphone. Its branch gets the <see cref="MicrophoneProcessing"/> and
    /// a gate, closed until a <see cref="VoiceGate"/> opens it; the gate stays even while it is turned
    /// off, so it can be turned on again without rebuilding. Either is left out when the runtime
    /// lacks its element.
    /// </param>
    /// <param name="noiseSuppression">How much background noise is taken out of a microphone.</param>
    /// <returns>A bin with a single src pad carrying RTP, or null when an element is missing.</returns>
    public static Bin? Build(OutboundTrack outbound, Element source, double volume, bool isMicrophone,
        NoiseSuppressionLevel noiseSuppression)
    {
        var payloadType = outbound.PayloadType > 0 ? outbound.PayloadType : DefaultOpusPayloadType;
        var isScreen = outbound.SourceType == MediaSourceType.Screen;

        // A runtime built before the gate has no level element; the voice still goes out, just ungated
        var isGated = isMicrophone && !isScreen && ElementFactory.Find("level") != null;

        var gate = isGated
            ? string.Format(CultureInfo.InvariantCulture, GateDescription, (ulong)VoiceGate.LevelInterval.TotalNanoseconds)
            : "";

        var encoding = Global.ParseBinFromDescription(string.Format(CultureInfo.InvariantCulture,
            isScreen ? ScreenEncodingDescription : EncodingDescription, volume, payloadType, outbound.SSRC, gate), true);

        if (encoding == null)
        {
            source.Dispose();
            return null;
        }

        encoding.SetProperty("name", EncodingName);

        var branch = Bin.New($"outgoing_{outbound.Id}");
        branch.Add(encoding);

        using (var output = encoding.GetStaticPad("src"))
        using (var ghost = output == null ? null : GhostPad.New("src", output))
        {
            if (ghost == null || !branch.AddPad(ghost))
            {
                encoding.Dispose();
                source.Dispose();
                branch.Dispose();
                return null;
            }
        }

        // The branch holds it from here on. A wrapper that is merely dropped keeps its element
        // alive until it is collected and the release queue of the binding is drained.
        encoding.Dispose();

        if (!ReplaceCapture(branch, source, isMicrophone && !isScreen, isScreen, noiseSuppression))
        {
            branch.Dispose();
            return null;
        }

        return branch;
    }

    /// <summary>
    /// Puts a capture part around <paramref name="source"/>, which it takes ownership of, in front
    /// of the rest of <paramref name="branch"/>, in place of the one it has. The branch may be running.
    /// </summary>
    /// <remarks>
    /// The previous part is stopped before it is taken out, and with it the thread that pushed into
    /// the rest of the branch, so nothing flows there in between and no pad has to be blocked.
    /// </remarks>
    /// <param name="isStereo">The capture keeps two channels, as the sound of a screen does.</param>
    /// <returns>False when the part could not be built or linked; the branch is then left without one.</returns>
    public static bool ReplaceCapture(Bin branch, Element source, bool isMicrophone, bool isStereo,
        NoiseSuppressionLevel noiseSuppression)
    {
        var capture = BuildCapture(source, isMicrophone, isStereo, noiseSuppression);

        if (capture == null)
            return false;

        using var encoding = branch.GetByName(EncodingName);

        if (encoding == null)
        {
            capture.Dispose();
            return false;
        }

        using (var previous = branch.GetByName(CaptureName))
        {
            if (previous != null)
            {
                PipelineBranch.Shutdown(previous);
                branch.Remove(previous);
            }
        }

        branch.Add(capture);

        if (!capture.Link(encoding))
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
        NoiseSuppressionLevel noiseSuppression)
    {
        // The fragment ends with a link, and without it the description would
        var description = string.Format(CaptureDescription,
            isMicrophone ? MicrophoneProcessing.Fragment(noiseSuppression) : "",
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
