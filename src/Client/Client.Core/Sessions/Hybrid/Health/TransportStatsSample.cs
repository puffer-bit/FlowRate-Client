namespace Client.Core.Sessions.Hybrid;

/// <summary>
/// How the media of a session fared between two readings of the transport statistics.
/// </summary>
/// <param name="RoundTripMs">
/// The round trip to the SFU from its receiver reports, or null while it sends us none: it reports
/// only about the streams we send.
/// </param>
/// <param name="InboundLossPercent">
/// The share of the packets sent to us that never came, lost on the way of their senders as well as on
/// ours; see <see cref="OwnInboundLossPercent"/>.
/// </param>
/// <param name="OutboundLossPercent">
/// The share of the packets of the videos we send that the SFU reports lost. Not of a voice: Opus is
/// sent with in-band FEC, and for such a stream mediasoup reports the worst loss of the ones who hear
/// it instead of its own, so the sender adds more FEC. A listener on a bad network showed our loss high.
/// </param>
/// <param name="Inbound">
/// Every stream we receive that carried packets before; a stream that never carried any is not in it.
/// </param>
/// <param name="IsOverTcp">The media goes over a TCP candidate pair, where a lost packet holds back the ones after it, or null while ICE has not picked a pair.</param>
public readonly record struct TransportStatsSample(double? RoundTripMs, double InboundLossPercent,
    double OutboundLossPercent, IReadOnlyList<InboundStreamSample>? Inbound = null, bool? IsOverTcp = null)
{
    private const double GoodRoundTripMs = 150;
    private const double FairRoundTripMs = 300;
    private const double GoodLossPercent = 2;
    private const double FairLossPercent = 8;

    // A sender with fewer packets in a reading tells too little of the network to be compared
    private const long MinPacketsForLoss = 20;

    /// <summary>
    /// The share of the packets sent to us lost on our own side of the SFU, as far as it can be told.
    /// </summary>
    /// <remarks>
    /// A stream we receive loses packets on the way of its sender to the SFU as well as on ours, and
    /// the SFU forwards them lost. What every sender loses alike is ours, so it is the smallest loss of
    /// the senders; a single sender cannot be told apart from us, and all of its loss counts.
    /// </remarks>
    public double OwnInboundLossPercent
    {
        get
        {
            var senders = LossByUser().Values.Where(user => user.Packets >= MinPacketsForLoss).ToList();
            return senders.Count > 0 ? senders.Min(user => user.LossPercent) : InboundLossPercent;
        }
    }

    public double LossPercent => Math.Max(OwnInboundLossPercent, OutboundLossPercent);

    /// <summary>
    /// The loss of each user's streams together, by the packets of each, and how many packets came.
    /// </summary>
    public Dictionary<int, (long Packets, double LossPercent)> LossByUser()
    {
        var totals = new Dictionary<int, (long Received, double Lost)>();

        foreach (var stream in Inbound ?? [])
        {
            // What the stream lost, back from its share of what was sent
            var lost = stream.LossPercent < 100 ? stream.Packets * stream.LossPercent / (100 - stream.LossPercent) : 0;
            var total = totals.GetValueOrDefault(stream.UserId);
            totals[stream.UserId] = (total.Received + stream.Packets, total.Lost + lost);
        }

        return totals.ToDictionary(pair => pair.Key, pair =>
        {
            var (received, lost) = pair.Value;
            return (received, received + lost > 0 ? 100 * lost / (received + lost) : 0);
        });
    }

    /// <summary>
    /// The share of the packets of a user lost on the way of that user to the SFU: what is lost of
    /// their streams beyond <see cref="OwnInboundLossPercent"/>. All of it while they are the only
    /// sender, see there.
    /// </summary>
    public double? SenderLossPercent(int userId)
    {
        var users = LossByUser();

        if (!users.TryGetValue(userId, out var user))
            return null;

        var senders = users.Values.Count(other => other.Packets >= MinPacketsForLoss);
        return senders > 1 ? Math.Max(0, user.LossPercent - OwnInboundLossPercent) : user.LossPercent;
    }

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
