using Client.Core.Sessions.Hybrid;
using Shared.Models.Media;

namespace Client.LibWebRTC.Voice;

/// <summary>
/// Turns the counters of every few readings of the statistics into what happened since the
/// previous sample, the way the GStreamer transport reports it in its TransportStats.
/// </summary>
/// <remarks>Not thread-safe: the caller runs every call under one lock.</remarks>
internal sealed class VoiceStatsSampler
{
    // The readings come for the speaking indicator, far more often than a sample is worth: one in
    // ten is the 2 s of the GStreamer transport
    public const int ReadingsPerSample = 10;

    private readonly Dictionary<uint, InboundCounters> _previousInbound = [];
    private readonly Dictionary<uint, long> _previousOutbound = [];
    private int _readings;

    /// <param name="findInbound">The inbound track with the given SSRC.</param>
    /// <param name="isOutbound">Whether we send a track with the given SSRC.</param>
    /// <returns>The sample, or null on a reading between two samples.</returns>
    public TransportStatsSample? Read(VoiceStreamCounters counters, Func<uint, InboundTrack?> findInbound,
        Func<uint, bool> isOutbound)
    {
        if (_readings++ % ReadingsPerSample != 0)
            return null;

        long received = 0;
        long lost = 0;
        var streams = new List<InboundStreamSample>();

        foreach (var current in counters.Inbound.Where(stream => stream.Packets > 0))
        {
            if (!_previousInbound.TryGetValue(current.Ssrc, out var last))
                continue;

            var packets = Math.Max(0, current.Packets - last.Packets);
            var missing = Math.Max(0, current.Lost - last.Lost);
            received += packets;
            lost += missing;

            if (findInbound(current.Ssrc) is { } track)
                streams.Add(new InboundStreamSample(track.UserId, track.SourceType, track.MediaType, packets,
                    packets + missing > 0 ? 100.0 * missing / (packets + missing) : 0, current.JitterMs,
                    (uint)Math.Max(0, current.KeyframeRequests - last.KeyframeRequests)));
        }

        // Only the streams that still carry packets: the SFU repeats its last report of a stopped one
        var reports = counters.Outbound
            .Where(stream => isOutbound(stream.Ssrc) && stream.Packets > _previousOutbound.GetValueOrDefault(stream.Ssrc, long.MaxValue))
            .ToList();

        var outboundLoss = reports.Select(stream => stream.FractionLost).OfType<double>().DefaultIfEmpty(0).Max() * 100;
        var roundTrips = reports.Select(stream => stream.RoundTripMs).OfType<double>().Where(ms => ms > 0).ToList();

        _previousInbound.Clear();
        _previousOutbound.Clear();

        foreach (var stream in counters.Inbound)
            _previousInbound[stream.Ssrc] = stream;

        foreach (var stream in counters.Outbound)
            _previousOutbound[stream.Ssrc] = stream.Packets;

        return new TransportStatsSample(roundTrips.Count > 0 ? roundTrips.Average() : null,
            received + lost > 0 ? 100.0 * lost / (received + lost) : 0, outboundLoss, streams, counters.IsOverTcp);
    }
}
