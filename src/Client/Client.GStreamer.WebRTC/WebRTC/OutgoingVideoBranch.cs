using System.Globalization;
using Gst;
using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Client.WebRTC.Sdp;
using Client.GStreamer.Video;
using Client.GStreamer.Interop;

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
    // timestamps follow. It holds about a fifth of a second of frames, and past that the frames up
    // to the next keyframe are dropped (see EncodedFrameGate): a late frame of a screen is worth
    // nothing. The packets wait in the send queue by time, not by count: a frame of a high rate
    // still takes a dozen of them, and the 200 a queue holds by default were gone in 90 ms
    private const string Description =
        "appsrc name=" + SourceName + " is-live=true format=time do-timestamp=true max-buffers={2} max-bytes=0 " +
        "leaky-type=downstream caps=\"{0}\" ! {1}" +
        "queue name=" + PipelineBranch.SendQueueName + " max-size-buffers=0 max-size-bytes=0 max-size-time=300000000";

    /// <summary>
    /// How many encoded frames wait for the packetizer before the frames up to the next keyframe are
    /// dropped: about 200 ms, and never fewer than 8.
    /// </summary>
    public static ulong MaxQueuedFrames(int framerate) => (ulong)Math.Max(8, framerate / 5);

    /// <summary>
    /// The codec the SFU created the producer with. Only legacy tracks without a MIME type default to H.264.
    /// </summary>
    public static VideoCodec CodecOf(OutboundTrack outbound) =>
        string.IsNullOrWhiteSpace(outbound.MimeType) ? VideoCodec.H264 :
            RtpCodec.VideoCodecOf(outbound.MimeType)
                ?? throw new NotSupportedException($"Unsupported video codec: {outbound.MimeType}");

    /// <param name="sequenceNumber">
    /// The RTP sequence number the track continues from, as <see cref="PipelineBranch.ReadNextSequenceNumber"/>
    /// gave it when the track was stopped, or -1 for a track that starts for the first time.
    /// </param>
    /// <returns>A bin with a single src pad carrying RTP, or null when an element is missing.</returns>
    public static Bin? Build(OutboundTrack outbound, VideoCodec codec, int framerate, int sequenceNumber = -1)
    {
        var format = VideoCodecPipeline.For(codec);
        var mtu = codec == VideoCodec.AV1 ? 1200 - Av1RtpDependencyDescriptor.MaxOverhead : 1200;
        var packetizer = string.Create(CultureInfo.InvariantCulture,
            $"{format.Payloader} name={PipelineBranch.PayloaderName} pt={outbound.PayloadType} ssrc={outbound.SSRC} seqnum-offset={sequenceNumber} timestamp-offset={outbound.SSRC} {format.PayloaderOptions} mtu={mtu} ! ");

        var branch = Global.ParseBinFromDescription(string.Format(CultureInfo.InvariantCulture, Description,
            format.Caps, packetizer, MaxQueuedFrames(framerate)), true);

        if (branch != null && codec == VideoCodec.AV1)
        {
            using var payloader = branch.GetByName(PipelineBranch.PayloaderName);
            if (payloader == null || !GstPacketTransform.Attach(payloader, "src", new Av1RtpDependencyDescriptor().Rewrite))
            {
                branch.Dispose();
                return null;
            }
        }
        branch?.SetName(PipelineBranch.OutgoingName(outbound.Id));
        return branch;
    }
}
