namespace Client.LibWebRTC.Voice;

/// <summary>
/// One reading of the statistics of a peer: how loud every voice is now, and the counters of the
/// streams where the peer reports them.
/// </summary>
/// <param name="Levels">The level of each section that plays a voice, and of the microphone.</param>
/// <param name="Streams">The counters, or null from a peer that reads levels only.</param>
public sealed record VoiceStats(IReadOnlyList<VoiceLevel> Levels, VoiceStreamCounters? Streams = null);

/// <param name="Mid">The section, or null for our microphone.</param>
/// <param name="Level">0..1, as the audioLevel of the statistics.</param>
public readonly record struct VoiceLevel(string? Mid, double Level);

/// <param name="IsOverTcp">The media goes over a TCP candidate pair.</param>
/// <param name="AvailableOutgoingKbps">What the bandwidth estimate lets us send, or null before there is one.</param>
public sealed record VoiceStreamCounters(IReadOnlyList<InboundCounters> Inbound, IReadOnlyList<OutboundCounters> Outbound,
    bool IsOverTcp, double? AvailableOutgoingKbps = null);

/// <summary>The counters of a stream we receive since it started.</summary>
/// <param name="KeyframeRequests">The keyframes we asked the sender of a picture for (PLI).</param>
/// <param name="Nacks">The NACKs we sent, each asking for lost packets again.</param>
/// <param name="Freezes">How often a picture stood still for long, as freezeCount counts it.</param>
public readonly record struct InboundCounters(uint Ssrc, long Packets, long Lost, double JitterMs, long KeyframeRequests = 0,
    long Nacks = 0, long Freezes = 0);

/// <summary>
/// The counters of a stream we send since it started, and what the SFU last reported of it.
/// </summary>
/// <param name="FractionLost">0..1, of the last receiver report, or null before one came.</param>
/// <param name="RoundTripMs">Of the last receiver report, or null before one came.</param>
/// <param name="Retransmitted">The packets sent again, on the RTX stream where there is one.</param>
/// <param name="Nacks">The NACKs the SFU sent us.</param>
/// <param name="KeyframeRequests">The keyframes the SFU asked us for (PLI).</param>
/// <param name="TargetKbps">What the encoder is asked for, or null for a stream without a target.</param>
public readonly record struct OutboundCounters(uint Ssrc, long Packets, double? FractionLost, double? RoundTripMs,
    long Retransmitted = 0, long Nacks = 0, long KeyframeRequests = 0, double? TargetKbps = null);
