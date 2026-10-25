namespace Client.Core.Sessions.Hybrid;

/// <summary>
/// How the media of a session fared between two readings of the transport statistics.
/// </summary>
/// <param name="RoundTripMs">
/// The round trip to the SFU from its receiver reports, or null while it sends us none: it reports
/// only about the streams we send.
/// </param>
/// <param name="InboundLossPercent">The share of the packets sent to us that never came.</param>
/// <param name="OutboundLossPercent">The share of the packets we sent that the SFU reports lost.</param>
public readonly record struct TransportStatsSample(double? RoundTripMs, double InboundLossPercent,
    double OutboundLossPercent)
{
    private const double GoodRoundTripMs = 150;
    private const double FairRoundTripMs = 300;
    private const double GoodLossPercent = 2;
    private const double FairLossPercent = 8;

    public double LossPercent => Math.Max(InboundLossPercent, OutboundLossPercent);

    /// <summary>
    /// The worse of what the round trip and the loss say. Loss is heard first, so it alone is enough
    /// for a rating when the round trip is unknown.
    /// </summary>
    public ConnectionQuality Quality
    {
        get
        {
            var roundTrip = RoundTripMs ?? 0;

            if (roundTrip < GoodRoundTripMs && LossPercent < GoodLossPercent)
                return ConnectionQuality.Good;

            if (roundTrip < FairRoundTripMs && LossPercent < FairLossPercent)
                return ConnectionQuality.Fair;

            return ConnectionQuality.Poor;
        }
    }
}

public enum ConnectionQuality
{
    Unknown,
    Good,
    Fair,
    Poor
}
