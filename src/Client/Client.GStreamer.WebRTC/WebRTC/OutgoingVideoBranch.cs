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
        "rtph264pay name=" + PipelineBranch.PayloaderName + " pt={0} ssrc={1} seqnum-offset={2} timestamp-offset={3} " +
        "config-interval=-1 aggregate-mode=zero-latency mtu=1200 ! ";

    // A picture id lets the receivers tell which frame a lost packet broke
    private const string Vp8Packetizer =
        "rtpvp8pay name=" + PipelineBranch.PayloaderName + " pt={0} ssrc={1} seqnum-offset={2} timestamp-offset={3} " +
        "picture-id-mode=15-bit mtu=1200 ! ";

    /// <summary>
    /// The codec the SFU created the producer with: VP8 when it says so, H.264 otherwise.
    /// </summary>
    public static VideoCodec CodecOf(OutboundTrack outbound) =>
        RtpCodec.EncodingNameOf(outbound.MimeType).Equals("VP8", StringComparison.OrdinalIgnoreCase)
            ? VideoCodec.VP8
            : VideoCodec.H264;

    /// <param name="sequenceNumber">
    /// The RTP sequence number the track continues from, as <see cref="PipelineBranch.ReadNextSequenceNumber"/>
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
