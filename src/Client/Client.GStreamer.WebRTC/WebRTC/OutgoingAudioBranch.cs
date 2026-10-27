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
    /// Name of the part after the capture: gain, gate, mute, encoder and payloader. It stays for the
    /// whole life of the track, so the RTP sequence and the timestamps go on across a replaced capture.
    /// </summary>
    private const string EncodingName = MicrophoneChain.TailName;

    // Mono speech at up to 64 kbit/s, less when the signal needs less. FEC lets the receiver rebuild
    // a lost packet from the next one, but the encoder only adds the redundancy for the loss it is
    // told to expect, and with the default of 0 it adds none. DTX shrinks the frames of silence to
    // a byte, refreshing the comfort noise every 400 ms. The payloader still sends every one of them:
    // opusdec only decodes the packets it gets, so with its own dtx dropping them the receiver
    // played 20 ms of comfort noise every 400 ms and silence between, which clicks through a long
    // pause, and the decoder held each refresh until the next one for its FEC, past the deadline
    // of the mixer. FEC, the loss it expects and DTX are what the media settings of the session
    // gave the track. The
    // gate and the mute silence the voice instead of dropping it: a hole in the stream would be
    // closed up by the encoder, the RTP clock would not advance over the pause, and the receiver
    // would replay the voice. The last queue drops the oldest packets past 200 ms rather than
    // sending them late.
    private const string EncodingDescription =
        "volume name=" + MicrophoneChain.InputVolumeName + " volume={0} ! {3}" +
        "volume name=" + MuteFadeName + " ! " +
        "volume name=" + MuteValveName + " mute=false ! " +
        "opusenc audio-type=voice bitrate=64000 {4} " +
        "frame-size=20 ! " +
        "rtpopuspay pt={1} ssrc={2} ! " +
        "queue name=" + PipelineBranch.SendQueueName + " max-size-buffers=0 max-size-bytes=0 max-size-time=200000000 leaky=downstream" +
        "{5}";

    // Stereo at a rate that keeps music clean. No DTX: a quiet passage of a film is not a pause,
    // and the producer of the screen sound is created without it. The volume and mute elements
    // are the same as the voice has, so the track is controlled the same way.
    private const string ScreenEncodingDescription =
        "volume name=" + MicrophoneChain.InputVolumeName + " volume={0} ! " +
        "volume name=" + MuteFadeName + " ! " +
        "volume name=" + MuteValveName + " mute=false ! " +
        "opusenc audio-type=generic bitrate=128000 {4} frame-size=20 ! " +
        "rtpopuspay pt={1} ssrc={2} ! " +
        "queue name=" + PipelineBranch.SendQueueName + " max-size-buffers=0 max-size-bytes=0 max-size-time=200000000 leaky=downstream";

    /// <summary>
    /// The FEC and DTX properties of the encoder. The sound of a screen never has DTX.
    /// </summary>
    private static string EncoderSettings(OutboundTrack outbound, bool isScreen) =>
        string.Format(CultureInfo.InvariantCulture, "inband-fec={0} packet-loss-percentage={1} dtx={2}",
            outbound.IsFecEnabled ? "true" : "false",
            outbound.IsFecEnabled ? Math.Clamp(outbound.FecLossPercent, 0, 100) : 0,
            DtxOf(outbound, isScreen));

    private static string DtxOf(OutboundTrack outbound, bool isScreen) =>
        outbound.IsDtxEnabled && !isScreen ? "true" : "false";

    /// <summary>
    /// Builds the branch around <paramref name="source"/>, which it takes ownership of.
    /// </summary>
    /// <param name="isMicrophone">
    /// Whether the source is a microphone. Its branch gets the <see cref="MicrophoneProcessing"/> and
    /// a gate, closed until a <see cref="VoiceGate"/> opens it, with the voice detection beside it;
    /// the gate stays even while it is turned off, so it can be turned on again without rebuilding.
    /// Each is left out when the runtime lacks its element.
    /// </param>
    /// <param name="noiseSuppression">How much background noise is taken out of a microphone.</param>
    /// <param name="echoProbe">The echo probe a microphone cancels the played voices with, if any.</param>
    /// <param name="isAutoGainControlEnabled">Whether a quiet microphone is brought up to the level of speech.</param>
    /// <returns>A bin with a single src pad carrying RTP, or null when an element is missing.</returns>
    public static Bin? Build(OutboundTrack outbound, Element source, double volume, bool isMicrophone,
        NoiseSuppressionLevel noiseSuppression, string? echoProbe, bool isAutoGainControlEnabled)
    {
        var payloadType = outbound.PayloadType > 0 ? outbound.PayloadType : DefaultOpusPayloadType;
        var isScreen = outbound.SourceType == MediaSourceType.Screen;
        var (gate, voiceDetection) = MicrophoneChain.DescribeGate(isMicrophone && !isScreen);

        var encoding = Global.ParseBinFromDescription(string.Format(CultureInfo.InvariantCulture,
            isScreen ? ScreenEncodingDescription : EncodingDescription, volume, payloadType, outbound.SSRC, gate,
            EncoderSettings(outbound, isScreen), voiceDetection), true);

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

        if (!MicrophoneChain.ReplaceCapture(branch, source, isMicrophone && !isScreen, isScreen, noiseSuppression, echoProbe,
                isAutoGainControlEnabled))
        {
            branch.Dispose();
            return null;
        }

        return branch;
    }
}
