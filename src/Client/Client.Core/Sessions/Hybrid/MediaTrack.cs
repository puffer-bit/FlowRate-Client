using Shared.Models.Media;

namespace Client.Core.Sessions.Hybrid;

/// <summary>
/// A media track of a hybrid session, as the SFU describes it.
/// </summary>
public abstract record MediaTrack
{
    public required string Id { get; init; }
    public required int UserId { get; init; }
    public required uint SSRC { get; init; }
    public required MediaSourceType SourceType { get; init; }
    public required MediaType MediaType { get; init; }
    public required string MimeType { get; init; }
    public required int PayloadType { get; init; }
    public required int ClockRate { get; init; }
    public int Channels { get; init; }
}

/// <summary>
/// A track another peer sends and we receive.
/// </summary>
public sealed record InboundTrack : MediaTrack
{
    /// <summary>
    /// The SFU resends lost packets of the track when asked with a NACK, on the same SSRC.
    /// </summary>
    public bool IsNackEnabled { get; init; }

    /// <summary>How long the packets of the track are held before they play, in ms; 0 for the default.</summary>
    public int JitterBufferMs { get; init; }
}

/// <summary>
/// A track we send to a producer the SFU created for it.
/// </summary>
public sealed record OutboundTrack : MediaTrack
{
    /// <summary>
    /// Payload type the lost packets of the track are resent with as RTX, or 0 when the SFU gave the
    /// track no RTX stream and they are not resent.
    /// </summary>
    public int RtxPayloadType { get; init; }

    /// <summary>SSRC of the RTX stream, or 0 when the track has none.</summary>
    public uint RtxSSRC { get; init; }

    /// <summary>An Opus track carries a copy of the previous packet in each one, to restore single losses.</summary>
    public bool IsFecEnabled { get; init; } = true;

    /// <summary>The loss, in percent, the Opus encoder prepares its FEC for.</summary>
    public int FecLossPercent { get; init; } = 10;

    /// <summary>A voice sends next to nothing while silent.</summary>
    public bool IsDtxEnabled { get; init; } = true;
}

/// <summary>
/// The producers of our screen share: the picture, and the sound when it was asked for.
/// </summary>
public sealed record ScreenShareOutbounds(OutboundTrack Video, OutboundTrack? Audio);

/// <summary>
/// The consumers of a screen share we watch: the picture, and the sound when the share carries it.
/// </summary>
public sealed record ScreenShareInbounds(InboundTrack Video, InboundTrack? Audio);
