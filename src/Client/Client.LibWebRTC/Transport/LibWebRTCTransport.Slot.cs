using Client.WebRTC.Sdp;

namespace Client.LibWebRTC.Transport;

public sealed partial class LibWebRTCTransport
{
    private const string AudioKind = "audio";
    private const string VideoKind = "video";

    /// <summary>
    /// One m-section. A sending one is only reused for sending, a receiving one for receiving, and
    /// each only for its kind.
    /// </summary>
    private sealed class Slot
    {
        public bool IsSend { get; init; }

        /// <summary>The kind of the m-line, audio or video.</summary>
        public string Kind { get; init; } = AudioKind;

        /// <summary>The section of m-line 0 that only brings the transport up.</summary>
        public bool IsPlaceholder { get; init; }

        public string? InboundId { get; set; }

        public string? OutboundId { get; set; }

        /// <summary>
        /// The codec of the track the section carried last; an emptied section keeps offering it.
        /// </summary>
        public RtpCodec? LastCodec { get; set; }

        /// <summary>
        /// The SSRC of the screen the section sent until it stopped, which the SFU only pauses. The
        /// section goes on sending, nothing: emptied, libwebrtc would end the stream with an RTCP BYE
        /// and start another one when the screen is back. Kept, the stream goes on with the next
        /// sequence number, as the SFU wants of a paused producer: it drops the packets after a jump,
        /// and those are the keyframe every viewer waits for.
        /// </summary>
        public uint? PausedSsrc { get; set; }

        /// <summary>The SSRC of the RTX stream of <see cref="PausedSsrc"/>, 0 for none.</summary>
        public uint PausedRtxSsrc { get; set; }

        // A native answer reuses its previously negotiated feedback for a codec. An offer can
        // therefore enable NACK while the answer and receive stream keep it disabled. Reuse
        // sections only with the same NACK setting; other sections stay inactive.
        public bool Carries(string mimeType, bool? nackEnabled = null) =>
            LastCodec == null ||
            ((string.IsNullOrEmpty(mimeType) ||
              LastCodec.EncodingName.Equals(RtpCodec.EncodingNameOf(mimeType), StringComparison.OrdinalIgnoreCase)) &&
             (nackEnabled == null || LastCodec.IsNackEnabled == nackEnabled));
    }
}
