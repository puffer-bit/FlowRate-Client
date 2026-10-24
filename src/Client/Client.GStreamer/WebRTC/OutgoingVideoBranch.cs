using System.Globalization;
using Gst;
using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Client.GStreamer.Video;
using Client.GStreamer.WebRTC.Sdp;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// Capture branch of an outbound screen track: the picture is paced and scaled, encoded with the codec
/// of the track, H.264 or VP8, and packetized with the payload type and SSRC the SFU created the
/// producer with. A second leg feeds
/// a sink with raw frames, so the sender sees what is being sent.
/// </summary>
/// <remarks>
/// The branch lives in the pipeline of webrtcbin on purpose: a keyframe request of the SFU, which
/// it sends for every viewer who joins, travels upstream from webrtcbin and reaches the encoder as
/// a force-key-unit event without any code here.
/// </remarks>
internal static class OutgoingVideoBranch
{
    // The sharer sees the capture at its own size and rate
    private const int PreviewMaxHeight = 16384;

    private const string PreviewSinkName = "preview";
    private const string PayloaderName = "payloader";

    // videorate turns the uneven capture into a steady rate, which the rate control of the encoders
    // relies on. Both legs drop instead of queueing: a late frame of a screen is worth nothing.
    //
    // Each leg scales for itself, after the tee: a limit placed before it is negotiated against
    // both legs, and the smaller preview would then shrink what is sent as well.
    //
    // The encoded picture is packetized by {3}, see Packetizer.
    private const string Description =
        "queue max-size-buffers=2 leaky=downstream ! " +
        "videoconvert ! videorate drop-only=true max-rate={0} ! tee name=split " +

        "split. ! queue max-size-buffers=1 leaky=downstream ! {6}" +
        "{2} ! " +
        "{3}" +
        "queue name=" + PipelineBranch.SendQueueName + " " +

        "split. ! queue max-size-buffers=1 leaky=downstream ! " +
        "videoscale ! videoconvert ! " +
        VideoFramePump.FrameCaps + ",height=[2,{4}],pixel-aspect-ratio=1/1 ! {5}";


    // The same branch for DMA-BUF frames of a capture, sent with a VA-API encoder: no CPU element
    // touches them, the postprocessor converts and scales for the encoder and for the preview alike
    private const string VaDmaBufDescription =
        "queue max-size-buffers=2 leaky=downstream ! " +
        "videorate drop-only=true max-rate={0} ! tee name=split " +

        "split. ! queue max-size-buffers=1 leaky=downstream ! {6}" +
        "{2} ! " +
        "{3}" +
        "queue name=" + PipelineBranch.SendQueueName + " " +

        "split. ! queue max-size-buffers=1 leaky=downstream ! " +
        "vapostproc ! " + VideoFramePump.FrameCaps + ",height=[2,{4}],pixel-aspect-ratio=1/1 ! {5}";

    // The same branch for a Direct3D 11 capture on Windows: the frames stay textures from the capture
    // to the encoder. d3d11convert scales and converts for the encoder, and the preview is scaled down
    // on the GPU before it is downloaded
    private const string D3D11Description =
        "queue max-size-buffers=2 leaky=downstream ! video/x-raw(memory:D3D11Memory) ! " +
        "videorate drop-only=true max-rate={0} ! tee name=split " +

        "split. ! queue max-size-buffers=1 leaky=downstream ! " +
        "d3d11convert ! video/x-raw(memory:D3D11Memory),format=NV12,height=[2,{1}],pixel-aspect-ratio=1/1 ! " +
        "{2} ! " +
        "{3}" +
        "queue name=" + PipelineBranch.SendQueueName + " " +

        "split. ! queue max-size-buffers=1 leaky=downstream ! " +
        "d3d11convert ! video/x-raw(memory:D3D11Memory),format=BGRA,height=[2,{4}],pixel-aspect-ratio=1/1 ! " +
        "d3d11download ! " + VideoFramePump.FrameCaps + " ! {5}";

    // Hardware encoders of Windows that take Direct3D 11 textures as they are
    private static readonly string[] D3D11Encoders = ["nvd3d11h264enc", "amfh264enc", "qsvh264enc", "mfh264enc"];

    // Constrained baseline is what the SFU router announces (profile-level-id 42e01f) and what the
    // software decoder of the viewers can always decode.
    private const string H264Packetizer =
        "video/x-h264,profile=constrained-baseline ! {0}" +
        "rtph264pay name=" + PayloaderName + " pt={1} ssrc={2} seqnum-offset={3} timestamp-offset={4} " +
        "config-interval=-1 aggregate-mode=zero-latency mtu=1200 ! ";

    // A picture id lets the receivers tell which frame a lost packet broke
    private const string Vp8Packetizer =
        "video/x-vp8 ! " +
        "rtpvp8pay name=" + PayloaderName + " pt={1} ssrc={2} seqnum-offset={3} timestamp-offset={4} " +
        "picture-id-mode=15-bit mtu=1200 ! ";

    // Scales and converts for the encoder: on the GPU where the encoder is a VA-API one, whose
    // postprocessor does both, otherwise on the CPU
    private const string CpuScale = "videoscale ! video/x-raw,height=[2,{0}],pixel-aspect-ratio=1/1 ! videoconvert ! ";
    private const string VaScale = "vapostproc ! video/x-raw(memory:VAMemory),height=[2,{0}],pixel-aspect-ratio=1/1 ! ";

    /// <summary>
    /// The sequence number the next branch of the same track has to start with.
    /// </summary>
    /// <remarks>
    /// A screen that is stopped and shown again keeps its producer on the SFU, so to the SFU it is
    /// one RTP stream. A payloader of its own picks a random sequence number, and the SFU drops
    /// the first packets after such a jump as out of order. Those packets are the keyframe every
    /// viewer waits for, so the picture would only come with the next one. The timestamps need no
    /// such care: they follow the running time of the pipeline from a fixed offset.
    /// </remarks>
    /// <returns>Null when the branch has sent nothing yet.</returns>
    public static int? ReadNextSequenceNumber(Bin branch)
    {
        using var payloader = branch.GetByName(PayloaderName);

        if (payloader == null || payloader.GetProperty<uint>("timestamp") == 0)
            return null;

        return (int)((payloader.GetProperty<uint>("seqnum") + 1) & 0xFFFF);
    }

    /// <param name="Bin">A bin with a single src pad carrying RTP.</param>
    /// <param name="Preview">Sink of the preview leg, for a <see cref="VideoFramePump"/>.</param>
    /// <param name="Scaler">Where the picture is scaled and converted for the encoder, for the log.</param>
    public sealed record Result(Bin Bin, Element Preview, VideoEncoderChoice Encoder, string Scaler);

    private static string Describe(OutboundTrack outbound, VideoCodec codec, ScreenShareSettings settings,
        VideoEncoderChoice encoder, string scale, int sequenceNumber, string description = Description) =>
        string.Format(CultureInfo.InvariantCulture, description, settings.Framerate, settings.MaxHeight,
            encoder.ToLaunchString(), Packetizer(outbound, codec, sequenceNumber), PreviewMaxHeight,
            VideoFramePump.SinkDescription(PreviewSinkName),
            string.Format(CultureInfo.InvariantCulture, scale, settings.MaxHeight));

    private static string Packetizer(OutboundTrack outbound, VideoCodec codec, int sequenceNumber)
    {
        var parser = VideoEncoderSelector.GetParser(codec) is { } parserFactory ? $"{parserFactory} ! " : "";

        return string.Format(CultureInfo.InvariantCulture, codec == VideoCodec.VP8 ? Vp8Packetizer : H264Packetizer,
            parser, outbound.PayloadType, outbound.SSRC, sequenceNumber, outbound.SSRC);
    }

    /// <summary>
    /// The codec the SFU created the producer with: VP8 when it says so, H.264 otherwise.
    /// </summary>
    public static VideoCodec CodecOf(OutboundTrack outbound) =>
        RtpCodec.EncodingNameOf(outbound.MimeType).Equals("VP8", StringComparison.OrdinalIgnoreCase)
            ? VideoCodec.VP8
            : VideoCodec.H264;

    // The encoder chosen in the settings is one of H.264
    private static VideoEncoderChoice? SelectEncoder(VideoCodec codec, int bitrateKbps) =>
        VideoEncoderSelector.Select(codec, bitrateKbps,
            codec == VideoCodec.H264 ? Client.Core.Settings.VideoSettings.Current.Encoder : null);

    /// <summary>
    /// Whether the branch would take DMA-BUF frames: only the VA-API encoder with its postprocessor does.
    /// </summary>
    public static bool CanSendDmaBuf(VideoCodec codec) =>
        SelectEncoder(codec, 1000) is { } encoder &&
        encoder.Factory.StartsWith("va", StringComparison.Ordinal) && ElementFactory.Find("vapostproc") != null;

    /// <summary>
    /// Builds the branch around <paramref name="source"/>, which it takes ownership of.
    /// </summary>
    /// <param name="settings">
    /// The height the picture is limited to, the rate it is paced at and the bitrate it is encoded with.
    /// </param>
    /// <param name="sequenceNumber">
    /// The RTP sequence number the track continues from, as <see cref="ReadNextSequenceNumber"/>
    /// gave it when the track was stopped, or -1 for a track that starts for the first time.
    /// </param>
    /// <param name="isD3D11Source">
    /// The source is a Direct3D 11 screen capture, whose textures can go to the encoder without
    /// passing through memory.
    /// </param>
    /// <param name="isDmaBufSource">
    /// The source may hand out DMA-BUF frames, see <see cref="CanSendDmaBuf"/>.
    /// </param>
    /// <returns>Null when an element is missing or cannot be linked.</returns>
    public static Result? Build(OutboundTrack outbound, Element source, ScreenShareSettings settings,
        int sequenceNumber = -1, bool isD3D11Source = false, bool isDmaBufSource = false)
    {
        var codec = CodecOf(outbound);
        var encoder = SelectEncoder(codec, settings.EffectiveBitrateKbps);

        if (encoder == null)
        {
            source.Dispose();
            return null;
        }

        var scale = encoder.Factory.StartsWith("va", StringComparison.Ordinal) && ElementFactory.Find("vapostproc") != null
            ? VaScale
            : CpuScale;
        var scaler = scale == VaScale ? "GPU (VA-API)" : "CPU";
        Bin? processing = null;

        // The preview leg ends in its sink, so the only open ends are the input and the RTP output
        if (isD3D11Source && D3D11Encoders.Contains(encoder.Factory) &&
            ElementFactory.Find("d3d11convert") != null && ElementFactory.Find("d3d11download") != null)
        {
            processing = Global.ParseBinFromDescription(
                Describe(outbound, codec, settings, encoder, CpuScale, sequenceNumber, D3D11Description), true);
            scaler = "GPU (Direct3D 11)";
        }

        if (processing == null && isDmaBufSource && scale == VaScale)
        {
            processing = Global.ParseBinFromDescription(
                Describe(outbound, codec, settings, encoder, scale, sequenceNumber, VaDmaBufDescription), true);
            scaler = "GPU (VA-API, DMA-BUF)";
        }

        if (processing == null)
        {
            processing = Global.ParseBinFromDescription(Describe(outbound, codec, settings, encoder, scale, sequenceNumber), true);
            scaler = scale == VaScale ? "GPU (VA-API)" : "CPU";
        }

        // The postprocessor may not take what the capture gives
        if (processing == null && scale != CpuScale)
        {
            scaler = "CPU";
            processing = Global.ParseBinFromDescription(Describe(outbound, codec, settings, encoder, CpuScale, sequenceNumber), true);
        }

        if (processing?.GetByName(PreviewSinkName) is not { } preview)
        {
            source.Dispose();
            processing?.Dispose();
            return null;
        }

        var branch = Bin.New($"outgoing_{outbound.Id}");
        branch.Add(source);
        branch.Add(processing);

        if (!source.Link(processing))
        {
            preview.Dispose();
            processing.Dispose();
            source.Dispose();
            branch.Dispose();
            return null;
        }

        using (var output = processing.GetStaticPad("src"))
        using (var ghost = output == null ? null : GhostPad.New("src", output))
        {
            if (ghost == null || !branch.AddPad(ghost))
            {
                preview.Dispose();
                processing.Dispose();
                source.Dispose();
                branch.Dispose();
                return null;
            }
        }

        // The branch holds them from here on. A wrapper that is merely dropped keeps its element
        // alive until it is collected and the release queue of the binding is drained.
        processing.Dispose();
        source.Dispose();

        return new Result(branch, preview, encoder, scaler);
    }
}
