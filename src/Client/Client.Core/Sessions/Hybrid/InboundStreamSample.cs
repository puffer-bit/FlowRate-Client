using Shared.Models.Media;

namespace Client.Core.Sessions.Hybrid;

/// <summary>
/// How one stream we receive fared between two readings of the transport statistics.
/// </summary>
/// <param name="Packets">The packets that came since the previous reading.</param>
/// <param name="LossPercent">The share of the packets sent to us that never came.</param>
/// <param name="JitterMs">How unevenly the packets come, as the stream reports it now.</param>
/// <param name="KeyframeRequests">The keyframes we asked the sender for since the previous reading.</param>
public readonly record struct InboundStreamSample(int UserId, MediaSourceType SourceType, MediaType MediaType,
    long Packets, double LossPercent, double JitterMs, uint KeyframeRequests);
