using System.Diagnostics;
using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Shared.Models.Media;

namespace Client.LibWebRTC.Voice;

/// <summary>
/// What the encoders, the decoders and libwebrtc measured of the streams of a transport, taken at the
/// moment of a reading of the statistics.
/// </summary>
/// <param name="Decoders">The decoder of each screen we watch and the timings of libwebrtc, by its SSRC.</param>
/// <param name="Encoders">The encoder of each screen we show and the timings of libwebrtc, by its SSRC.</param>
internal sealed record MediaPerfSources(
    Func<uint, InboundTrack?> FindInbound,
    Func<uint, OutboundTrack?> FindOutbound,
    IReadOnlyDictionary<uint, (DecoderPerf? Decoder, VideoReceiveTiming? Timing)> Decoders,
    IReadOnlyDictionary<uint, (EncoderPerf? Encoder, VideoSendTiming? Timing)> Encoders);

/// <summary>
/// Turns two readings of the statistics of libwebrtc into what happened between them: the counters of
/// the RTCStats objects become rates and averages over the window, as their specification means them
/// (a total delay divided by the count it adds up, such as jitterBufferDelay by jitterBufferEmittedCount).
/// </summary>
/// <remarks>Not thread-safe: the caller runs every call under one lock or on one thread.</remarks>
internal sealed class MediaPerfSampler
{
    // Set by the passthrough decoder of flowrate_video.cc, whose decoding takes no time
    private const string PassthroughDecoder = "FlowRatePassthrough";

    private Dictionary<string, StatsEntry> _previous = [];
    private long _previousAt;

    /// <returns>The window since the previous reading, or null on the first one.</returns>
    public MediaPerfReport? Read(IReadOnlyList<StatsEntry> entries, MediaPerfSources sources)
    {
        var now = Stopwatch.GetTimestamp();
        var current = entries.GroupBy(entry => entry.Id).ToDictionary(group => group.Key, group => group.Last());
        var previous = _previous;
        var seconds = _previousAt == 0 ? 0 : Stopwatch.GetElapsedTime(_previousAt, now).TotalSeconds;

        _previous = current;
        _previousAt = now;

        if (seconds <= 0)
            return null;

        var window = new Window(previous, seconds);
        var streams = new Dictionary<(int UserId, MediaSourceType SourceType), StreamPerf>();
        var voices = new Dictionary<int, AudioReceivePerf>();
        var remote = entries.Where(entry => entry.Type == "remote-inbound-rtp" && entry.Number("ssrc") is not null)
            .GroupBy(entry => (uint)entry.Number("ssrc")!.Value)
            .ToDictionary(group => group.Key, group => group.Last());

        foreach (var entry in entries)
        {
            if (entry.Number("ssrc") is not { } number)
                continue;

            var ssrc = (uint)number;

            switch (entry.Type)
            {
                case "inbound-rtp" when sources.FindInbound(ssrc) is { } inbound:
                    var key = (inbound.UserId, inbound.SourceType);

                    if (inbound.MediaType == MediaType.Video)
                    {
                        var (decoder, timing) = sources.Decoders.GetValueOrDefault(ssrc);
                        streams[key] = Stream(streams, key, false) with
                        {
                            Receive = window.VideoReceive(entry, timing),
                            Decoder = decoder
                        };
                    }
                    else if (inbound.SourceType == MediaSourceType.Microphone)
                    {
                        voices[inbound.UserId] = window.AudioReceive(entry);
                    }
                    else
                    {
                        streams[key] = Stream(streams, key, false) with { Audio = window.AudioReceive(entry) };
                    }

                    break;

                case "outbound-rtp" when entry.Text("kind") == "video" && sources.FindOutbound(ssrc) is { } outbound:
                    var (encoder, sendTiming) = sources.Encoders.GetValueOrDefault(ssrc);
                    var ownKey = (outbound.UserId, outbound.SourceType);
                    streams[ownKey] = Stream(streams, ownKey, true) with
                    {
                        Send = window.VideoSend(entry, remote.GetValueOrDefault(ssrc), sendTiming),
                        Encoder = encoder
                    };
                    break;
            }
        }

        // A screen whose statistics libwebrtc does not report yet still has its encoder or decoder
        foreach (var (ssrc, (decoder, timing)) in sources.Decoders)
        {
            if (sources.FindInbound(ssrc) is { } inbound && !streams.ContainsKey((inbound.UserId, inbound.SourceType)) &&
                (decoder != null || timing != null))
                streams[(inbound.UserId, inbound.SourceType)] =
                    new StreamPerf(inbound.UserId, inbound.SourceType, false, Decoder: decoder);
        }

        foreach (var (ssrc, (encoder, _)) in sources.Encoders)
        {
            if (sources.FindOutbound(ssrc) is { } outbound && encoder != null &&
                !streams.ContainsKey((outbound.UserId, outbound.SourceType)))
                streams[(outbound.UserId, outbound.SourceType)] =
                    new StreamPerf(outbound.UserId, outbound.SourceType, true, Encoder: encoder);
        }

        return new MediaPerfReport("libwebrtc", TimeSpan.FromSeconds(seconds), window.Network(current),
            streams.Values.ToList(), voices);
    }

    private static StreamPerf Stream(Dictionary<(int UserId, MediaSourceType SourceType), StreamPerf> streams,
        (int UserId, MediaSourceType SourceType) key, bool isOwn) =>
        streams.TryGetValue(key, out var stream) ? stream : new StreamPerf(key.UserId, key.SourceType, isOwn);

    /// <summary>The differences of the counters between the previous reading and this one.</summary>
    private sealed class Window(Dictionary<string, StatsEntry> previous, double seconds)
    {
        // A counter that went back, as one of a stream that restarted, counts from 0
        private double Delta(StatsEntry entry, string name)
        {
            var now = entry.Number(name) ?? 0;
            var before = previous.TryGetValue(entry.Id, out var last) ? last.Number(name) ?? 0 : 0;

            return now >= before ? now - before : now;
        }

        private double PerSecond(StatsEntry entry, string name) => Delta(entry, name) / seconds;

        private double Kbps(StatsEntry entry, string name) => Delta(entry, name) * 8 / 1000 / seconds;

        // The average of a total of seconds over the count it adds up, in ms
        private double? AverageMs(StatsEntry entry, string total, string count) =>
            Delta(entry, count) is > 0 and var counted ? Delta(entry, total) / counted * 1000 : null;

        private double LossPercent(StatsEntry entry)
        {
            var received = Delta(entry, "packetsReceived");
            var lost = Math.Max(0, Delta(entry, "packetsLost"));

            return received + lost > 0 ? 100 * lost / (received + lost) : 0;
        }

        public VideoReceivePerf VideoReceive(StatsEntry entry, VideoReceiveTiming? timing)
        {
            var implementation = entry.Text("decoderImplementation");
            var decodesItself = implementation != null && implementation != PassthroughDecoder;

            return new VideoReceivePerf(
                Kbps(entry, "bytesReceived"),
                (long)Delta(entry, "packetsReceived"),
                LossPercent(entry),
                (entry.Number("jitter") ?? 0) * 1000,
                AverageMs(entry, "jitterBufferDelay", "jitterBufferEmittedCount") ?? 0,
                AverageMs(entry, "jitterBufferTargetDelay", "jitterBufferEmittedCount"),
                AverageMs(entry, "totalAssemblyTime", "framesAssembledFromMultiplePackets") ?? 0,
                AverageMs(entry, "totalProcessingDelay", "framesDecoded") ?? 0,
                PerSecond(entry, "framesReceived"),
                PerSecond(entry, "framesDecoded"),
                (long)Delta(entry, "framesDropped"),
                (int)Delta(entry, "freezeCount"),
                Delta(entry, "totalFreezesDuration") * 1000,
                (int)Delta(entry, "nackCount"),
                (int)Delta(entry, "pliCount"),
                (int)Delta(entry, "firCount"),
                (int)Delta(entry, "keyFramesDecoded"),
                // The passthrough decoder does not know the size of the pictures it hands on
                decodesItself ? (int)(entry.Number("frameWidth") ?? 0) : 0,
                decodesItself ? (int)(entry.Number("frameHeight") ?? 0) : 0,
                decodesItself ? implementation : null,
                decodesItself ? AverageMs(entry, "totalDecodeTime", "framesDecoded") : null,
                timing?.Wait,
                timing?.Handoff);
        }

        public AudioReceivePerf AudioReceive(StatsEntry entry)
        {
            var samples = Delta(entry, "totalSamplesReceived");

            double Share(string name) => samples > 0 ? 100 * Delta(entry, name) / samples : 0;

            return new AudioReceivePerf(
                Kbps(entry, "bytesReceived"),
                LossPercent(entry),
                (entry.Number("jitter") ?? 0) * 1000,
                AverageMs(entry, "jitterBufferDelay", "jitterBufferEmittedCount") ?? 0,
                AverageMs(entry, "jitterBufferTargetDelay", "jitterBufferEmittedCount"),
                Share("concealedSamples"),
                Share("removedSamplesForAcceleration"),
                Share("insertedSamplesForDeceleration"),
                (long)Delta(entry, "fecPacketsReceived"));
        }

        public VideoSendPerf VideoSend(StatsEntry entry, StatsEntry? remote, VideoSendTiming? timing) => new(
            Kbps(entry, "bytesSent"),
            entry.Number("targetBitrate") is { } target ? target / 1000 : null,
            PerSecond(entry, "framesSent"),
            AverageMs(entry, "totalPacketSendDelay", "packetsSent") ?? 0,
            (long)Delta(entry, "retransmittedPacketsSent"),
            (int)Delta(entry, "nackCount"),
            (int)Delta(entry, "pliCount"),
            (int)Delta(entry, "firCount"),
            (int)Delta(entry, "hugeFramesSent"),
            entry.Text("qualityLimitationReason"),
            remote?.Number("fractionLost") is { } fraction ? fraction * 100 : null,
            remote?.Number("roundTripTime") is { } roundTrip ? roundTrip * 1000 : null,
            timing?.Queue,
            timing is { } send ? Math.Max(0, send.Pushed - send.Encoded) : null);

        public NetworkPerf? Network(Dictionary<string, StatsEntry> current)
        {
            var pairId = current.Values.FirstOrDefault(entry => entry.Type == "transport")?.Text("selectedCandidatePairId");

            if (pairId == null || !current.TryGetValue(pairId, out var pair))
                return null;

            var local = pair.Text("localCandidateId") is { } localId ? current.GetValueOrDefault(localId) : null;
            var remote = pair.Text("remoteCandidateId") is { } remoteId ? current.GetValueOrDefault(remoteId) : null;
            bool? isOverTcp = local?.Text("protocol") is { } localProtocol && remote?.Text("protocol") is { } remoteProtocol
                ? localProtocol == "tcp" || remoteProtocol == "tcp"
                : null;

            return new NetworkPerf(
                pair.Number("currentRoundTripTime") is { } roundTrip ? roundTrip * 1000 : null,
                pair.Number("availableOutgoingBitrate") is { } available ? available / 1000 : null,
                Kbps(pair, "bytesSent"),
                Kbps(pair, "bytesReceived"),
                isOverTcp,
                local?.Text("candidateType"),
                remote?.Text("candidateType"));
        }
    }
}
