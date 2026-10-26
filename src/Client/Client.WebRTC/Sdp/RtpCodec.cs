using Shared.Models.Media;
using Client.Core.Sessions.Hybrid;

namespace Client.WebRTC.Sdp;

/// <summary>
/// Codec description of a single m-section.
/// </summary>
/// <remarks>
/// These values have to match the ones the SFU announced in the rtpParameters of the
/// corresponding producer or consumer. The presets below are only defaults for the cases where
/// the negotiation does not carry them yet.
/// </remarks>
public sealed record RtpCodec
{
    public required int PayloadType { get; init; }

    /// <summary>
    /// Encoding name as it appears in the rtpmap attribute, for example H264 or opus.
    /// </summary>
    public required string EncodingName { get; init; }

    public required int ClockRate { get; init; }

    /// <summary>
    /// Channel count for audio codecs. Video codecs leave it unset.
    /// </summary>
    public int? Channels { get; init; }

    /// <summary>
    /// Value of the fmtp attribute, without the payload type prefix.
    /// </summary>
    public string? FormatParameters { get; init; }

    /// <summary>
    /// Values of the rtcp-fb attributes, without the payload type prefix.
    /// </summary>
    public IReadOnlyList<string> RtcpFeedback { get; init; } = [];

    public static RtpCodec H264(int payloadType = 96) => new()
    {
        PayloadType = payloadType,
        EncodingName = "H264",
        ClockRate = 90000,
        FormatParameters = "level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f",
        RtcpFeedback = ["nack", "nack pli", "ccm fir", "goog-remb"]
    };

    public static RtpCodec VP8(int payloadType = 97) => new()
    {
        PayloadType = payloadType,
        EncodingName = "VP8",
        ClockRate = 90000,
        RtcpFeedback = ["nack", "nack pli", "ccm fir", "goog-remb"]
    };

    public static RtpCodec VP9(int payloadType = 98) => new()
    {
        PayloadType = payloadType,
        EncodingName = "VP9",
        ClockRate = 90000,
        FormatParameters = "profile-id=0",
        RtcpFeedback = ["nack", "nack pli", "ccm fir", "goog-remb"]
    };

    public static RtpCodec AV1(int payloadType = 99) => new()
    {
        PayloadType = payloadType,
        EncodingName = "AV1",
        ClockRate = 90000,
        RtcpFeedback = ["nack", "nack pli", "ccm fir", "goog-remb"]
    };

    public static RtpCodec Opus(int payloadType = 111) => new()
    {
        PayloadType = payloadType,
        EncodingName = "opus",
        ClockRate = 48000,
        Channels = 2,
        FormatParameters = "minptime=10;useinbandfec=1",
        RtcpFeedback = ["transport-cc"]
    };

    /// <summary>
    /// The codec the SFU announced for an inbound track. Falls back to the presets when the server
    /// does not report codecs yet. Lost packets are asked for again only where the SFU keeps them.
    /// </summary>
    public static RtpCodec FromInbound(InboundTrack inbound) =>
        FromParameters(inbound.MediaType, inbound.MimeType, inbound.PayloadType, inbound.ClockRate, inbound.Channels,
            inbound.IsNackEnabled);

    /// <summary>
    /// The codec the SFU expects on an outbound track. The SFU asks for lost packets again only
    /// where the track has an RTX stream to resend them on.
    /// </summary>
    public static RtpCodec FromOutbound(OutboundTrack outbound) =>
        FromParameters(outbound.MediaType, outbound.MimeType, outbound.PayloadType, outbound.ClockRate, outbound.Channels,
            outbound.RtxPayloadType != 0);

    /// <summary>The encoding name of a MIME type such as video/VP8, or the whole of it without a slash.</summary>
    public static string EncodingNameOf(string mimeType)
    {
        var slash = mimeType.IndexOf('/');
        return slash >= 0 ? mimeType[(slash + 1)..] : mimeType;
    }

    /// <summary>Whether the codec has lost packets asked for again.</summary>
    public bool IsNackEnabled => RtcpFeedback.Contains(NackFeedback);

    private const string NackFeedback = "nack";

    private static RtpCodec FromParameters(MediaType mediaType, string mimeType, int payloadType, int clockRate, int channels,
        bool nack)
    {
        var preset = mediaType == MediaType.Audio ? Opus() : H264();

        if (string.IsNullOrWhiteSpace(mimeType) || payloadType <= 0 || clockRate <= 0)
            return preset;

        var encodingName = EncodingNameOf(mimeType);

        if (mediaType == MediaType.Audio)
        {
            return preset with
            {
                PayloadType = payloadType,
                EncodingName = encodingName,
                ClockRate = clockRate,
                Channels = channels > 0 ? channels : preset.Channels,
                RtcpFeedback = nack ? [NackFeedback, ..preset.RtcpFeedback] : preset.RtcpFeedback
            };
        }

        return new RtpCodec
        {
            PayloadType = payloadType,
            EncodingName = encodingName,
            ClockRate = clockRate,
            // The SFU does not pass the format parameters on, and without them webrtcbin would
            // negotiate H.264 in packetization mode 0, which the payloader does not produce
            FormatParameters = encodingName.Equals("H264", StringComparison.OrdinalIgnoreCase)
                ? H264().FormatParameters
                : null,
            // Key frames are asked for either way
            RtcpFeedback = nack ? [NackFeedback, "nack pli", "ccm fir"] : ["nack pli", "ccm fir"]
        };
    }

    public string ToRtpMap() => Channels.HasValue
        ? $"{PayloadType} {EncodingName}/{ClockRate}/{Channels}"
        : $"{PayloadType} {EncodingName}/{ClockRate}";
}
