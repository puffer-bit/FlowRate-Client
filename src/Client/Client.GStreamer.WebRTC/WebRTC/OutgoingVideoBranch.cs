using System.Globalization;
using Gst;
using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Client.WebRTC.Sdp;

namespace Client.GStreamer.WebRTC;

/// <summary>
/// Sending branch of an outbound screen track: the encoded frames of its capture are pushed into an
/// appsrc and packetized with the payload type and SSRC the SFU created the producer with.
/// </summary>
/// <remarks>
/// A keyframe request of the SFU, which it sends for every viewer who joins, travels upstream from
/// webrtcbin to the appsrc; the transport passes it on to the capture from there.
/// </remarks>
internal static class OutgoingVideoBranch
{
    /// <summary>Name of the appsrc the encoded frames are pushed into.</summary>
    public const string SourceName = "encoded";

    private const string PayloaderName = "payloader";

    // The appsrc stamps the frames with the running time of the pipeline of webrtcbin, which the RTP
    // timestamps follow. It drops the oldest frames rather than queueing them: a late frame of a
    // screen is worth nothing.
    private const string Description =
        "appsrc name=" + SourceName + " is-live=true format=time do-timestamp=true max-buffers=8 max-bytes=0 " +
        "leaky-type=downstream caps=\"{0}\" ! {1}" +
        "queue name=" + PipelineBranch.SendQueueName;

    // Constrained baseline is what the SFU router announces (profile-level-id 42e01f). The parameter
    // sets come with every keyframe already, and are sent again before each one all the same.
    private const string H264Caps = "video/x-h264,stream-format=byte-stream,alignment=au,profile=constrained-baseline";

    private const string H264Packetizer =
        "rtph264pay name=" + PayloaderName + " pt={0} ssrc={1} seqnum-offset={2} timestamp-offset={3} " +
        "config-interval=-1 aggregate-mode=zero-latency mtu=1200 ! ";

    // A picture id lets the receivers tell which frame a lost packet broke
    private const string Vp8Packetizer =
        "rtpvp8pay name=" + PayloaderName + " pt={0} ssrc={1} seqnum-offset={2} timestamp-offset={3} " +
        "picture-id-mode=15-bit mtu=1200 ! ";

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

    /// <summary>
    /// The codec the SFU created the producer with: VP8 when it says so, H.264 otherwise.
    /// </summary>
    public static VideoCodec CodecOf(OutboundTrack outbound) =>
        RtpCodec.EncodingNameOf(outbound.MimeType).Equals("VP8", StringComparison.OrdinalIgnoreCase)
            ? VideoCodec.VP8
            : VideoCodec.H264;

    /// <param name="sequenceNumber">
    /// The RTP sequence number the track continues from, as <see cref="ReadNextSequenceNumber"/>
    /// gave it when the track was stopped, or -1 for a track that starts for the first time.
    /// </param>
    /// <returns>A bin with a single src pad carrying RTP, or null when an element is missing.</returns>
    public static Bin? Build(OutboundTrack outbound, VideoCodec codec, int sequenceNumber = -1)
    {
        var packetizer = string.Format(CultureInfo.InvariantCulture, codec == VideoCodec.VP8 ? Vp8Packetizer : H264Packetizer,
            outbound.PayloadType, outbound.SSRC, sequenceNumber, outbound.SSRC);

        var branch = Global.ParseBinFromDescription(string.Format(CultureInfo.InvariantCulture, Description,
            codec == VideoCodec.VP8 ? "video/x-vp8" : H264Caps, packetizer), true);

        branch?.SetName($"outgoing_{outbound.Id}");
        return branch;
    }
}
