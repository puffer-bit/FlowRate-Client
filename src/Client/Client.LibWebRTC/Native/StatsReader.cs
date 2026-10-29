using System.Globalization;
using System.Text.Json;
using Client.LibWebRTC.Voice;

namespace Client.LibWebRTC.Native;

/// <summary>
/// Reads the levels and the counters out of a report of the statistics of libwebrtc: a JSON array
/// of the RTCStats objects of the WebRTC specification, and of the levels flowrate_webrtc adds.
/// </summary>
/// <remarks>
/// The levels of the voices are the ones of flowrate_webrtc, not the audioLevel of the statistics:
/// libwebrtc takes that one after the playback volume, so a voice turned down would read quieter
/// and a muted one would never read as speaking.
/// </remarks>
internal static class StatsReader
{
    public static VoiceStats Read(string json)
    {
        using var document = JsonDocument.Parse(json);

        var levels = new List<VoiceLevel>();
        var inbound = new List<InboundCounters>();
        var sent = new Dictionary<uint, OutboundCounters>();
        var remote = new Dictionary<uint, (double? FractionLost, double? RoundTripMs)>();
        var candidates = new Dictionary<string, string>();
        var pairs = new Dictionary<string, (string? Local, string? Remote)>();
        var available = new Dictionary<string, double>();
        string? selectedPair = null;

        foreach (var stats in document.RootElement.EnumerateArray())
        {
            var kind = String(stats, "kind");

            switch (String(stats, "type"))
            {
                case "flowrate-voice-level":
                    if (Number(stats, "peak") is { } peak && String(stats, "mid") is { } mid)
                        levels.Add(new VoiceLevel(mid, Math.Min(1, peak / short.MaxValue)));
                    break;

                // The voices and the screens alike, as the GStreamer transport reports them
                case "inbound-rtp":
                    if (Number(stats, "ssrc") is { } ssrc)
                        inbound.Add(new InboundCounters((uint)ssrc, (long)(Number(stats, "packetsReceived") ?? 0),
                            (long)(Number(stats, "packetsLost") ?? 0), (Number(stats, "jitter") ?? 0) * 1000,
                            (long)(Number(stats, "pliCount") ?? 0), (long)(Number(stats, "nackCount") ?? 0),
                            (long)(Number(stats, "freezeCount") ?? 0)));
                    break;

                case "media-source" when kind == "audio":
                    if (Number(stats, "audioLevel") is { } localLevel)
                        levels.Add(new VoiceLevel(null, localLevel));
                    break;

                case "outbound-rtp":
                    if (Number(stats, "ssrc") is { } sentSsrc)
                        sent[(uint)sentSsrc] = new OutboundCounters((uint)sentSsrc, (long)(Number(stats, "packetsSent") ?? 0),
                            null, null, (long)(Number(stats, "retransmittedPacketsSent") ?? 0),
                            (long)(Number(stats, "nackCount") ?? 0), (long)(Number(stats, "pliCount") ?? 0),
                            Number(stats, "targetBitrate") / 1000);
                    break;

                case "remote-inbound-rtp":
                    if (Number(stats, "ssrc") is { } reportedSsrc)
                        remote[(uint)reportedSsrc] = (Number(stats, "fractionLost"),
                            Number(stats, "roundTripTime") is { } roundTrip ? roundTrip * 1000 : null);
                    break;

                case "transport":
                    selectedPair = String(stats, "selectedCandidatePairId") ?? selectedPair;
                    break;

                case "candidate-pair":
                    if (String(stats, "id") is { } pairId)
                    {
                        pairs[pairId] = (String(stats, "localCandidateId"), String(stats, "remoteCandidateId"));

                        if (Number(stats, "availableOutgoingBitrate") is { } bitrate)
                            available[pairId] = bitrate / 1000;
                    }
                    break;

                case "local-candidate" or "remote-candidate":
                    if (String(stats, "id") is { } candidateId && String(stats, "protocol") is { } protocol)
                        candidates[candidateId] = protocol;
                    break;
            }
        }

        var outbound = sent.Values
            .Select(stream => remote.TryGetValue(stream.Ssrc, out var report)
                ? stream with { FractionLost = report.FractionLost, RoundTripMs = report.RoundTripMs }
                : stream)
            .ToList();

        // Unknown until ICE selects a pair whose candidates are reported
        bool? isOverTcp = selectedPair != null && pairs.TryGetValue(selectedPair, out var pair)
                                               && Protocol(pair.Local) is { } localProtocol
                                               && Protocol(pair.Remote) is { } remoteProtocol
            ? localProtocol == "tcp" || remoteProtocol == "tcp"
            : null;

        return new VoiceStats(levels, new VoiceStreamCounters(inbound, outbound, isOverTcp,
            selectedPair != null && available.TryGetValue(selectedPair, out var kbps) ? kbps : null));

        string? Protocol(string? candidateId) =>
            candidateId == null ? null : candidates.GetValueOrDefault(candidateId);
    }

    private static string? String(JsonElement stats, string name) =>
        stats.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // The counters of 64 bits come as numbers or, in some versions, as strings
    private static double? Number(JsonElement stats, string name)
    {
        if (!stats.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture,
                out var number) => number,
            _ => null
        };
    }
}
