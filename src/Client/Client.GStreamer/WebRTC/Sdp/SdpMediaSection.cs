namespace Client.GStreamer.WebRTC.Sdp;

/// <summary>
/// Direction of an m-section as seen by the remote side.
/// </summary>
public enum SdpDirection
{
    /// <summary>The SFU sends this stream, we receive it. Used for consumers.</summary>
    SendOnly,

    /// <summary>The SFU receives this stream, we send it. Used for producers.</summary>
    RecvOnly,

    /// <summary>
    /// The section carries nothing. Sections of departed peers are kept this way instead of being
    /// removed, so that the mid of every remaining section stays stable, and are reused by the
    /// next peer that arrives.
    /// </summary>
    Inactive
}

/// <summary>
/// One m-section of the remote description. mediasoup needs a separate section per producer and
/// per consumer, and its mid has to match the mid of the matching rtpParameters.
/// </summary>
public sealed record SdpMediaSection
{
    /// <summary>
    /// Value of the mid attribute. It also takes part in the BUNDLE group.
    /// </summary>
    public required string Mid { get; init; }

    /// <summary>
    /// Media kind as it appears in the m-line, either video or audio.
    /// </summary>
    public required string Kind { get; init; }

    public required SdpDirection Direction { get; init; }

    public required RtpCodec Codec { get; init; }

    /// <summary>
    /// SSRC of the stream. Only meaningful for sections the SFU sends.
    /// </summary>
    public uint? SSRC { get; init; }

    public string Cname { get; init; } = "sfu";

    /// <summary>
    /// Id of the consumer the section carries, null for a section the SFU sends nothing in. Not
    /// a part of the description.
    /// </summary>
    public string? InboundId { get; init; }

    public string ToDirectionAttribute() => Direction switch
    {
        SdpDirection.SendOnly => "sendonly",
        SdpDirection.RecvOnly => "recvonly",
        _ => "inactive"
    };
}
