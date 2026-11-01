using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Client.LibWebRTC.Voice;
using Shared.Models.Media;
using Xunit;

namespace Client.Application.Tests;

public class MediaPerfSamplerTests
{
    private const uint ScreenSsrc = 9;
    private const uint OurScreenSsrc = 77;

    private static readonly InboundTrack Screen = new()
    {
        Id = "in", UserId = 8, SSRC = ScreenSsrc, SourceType = MediaSourceType.Screen, MediaType = MediaType.Video,
        MimeType = "video/H264", PayloadType = 102, ClockRate = 90000
    };

    private static readonly OutboundTrack OurScreen = new()
    {
        Id = "out", UserId = 3, SSRC = OurScreenSsrc, SourceType = MediaSourceType.Screen, MediaType = MediaType.Video,
        MimeType = "video/H264", PayloadType = 102, ClockRate = 90000
    };

    private static readonly MediaPerfSources NoCodecs = new(
        ssrc => ssrc == ScreenSsrc ? Screen : null,
        ssrc => ssrc == OurScreenSsrc ? OurScreen : null,
        new Dictionary<uint, (DecoderPerf?, VideoReceiveTiming?)>(),
        new Dictionary<uint, (EncoderPerf?, VideoSendTiming?)>());

    [Fact]
    public void FirstReadingOnlyStartsTheWindow()
    {
        var sampler = new MediaPerfSampler();

        Assert.Null(sampler.Read(Readings(0), NoCodecs));
    }

    [Fact]
    public void CountersBecomeAveragesOfTheWindow()
    {
        var sampler = new MediaPerfSampler();
        sampler.Read(Readings(0), NoCodecs);
        Thread.Sleep(5);

        var report = Assert.IsType<MediaPerfReport>(sampler.Read(Readings(1), NoCodecs));
        var receive = Assert.IsType<VideoReceivePerf>(report.Find(Screen.UserId, MediaSourceType.Screen)?.Receive);

        // 90 packets arrived and 10 were lost between the readings
        Assert.Equal(10, receive.LossPercent, 3);
        Assert.Equal(90, receive.Packets);

        // 3 s of waiting over 60 pictures, 1.2 s of processing over 30 decoded ones
        Assert.Equal(50, receive.JitterBufferMs, 3);
        Assert.Equal(40, receive.ProcessingMs, 3);
        Assert.Equal(1, receive.Freezes);
        Assert.Equal(250, receive.FreezeMs, 3);

        // The passthrough decoder of the desktop decodes nothing itself
        Assert.Null(receive.DecoderImplementation);
        Assert.Null(receive.DecodeMs);

        var send = Assert.IsType<VideoSendPerf>(report.Streams.Single(stream => stream.IsOwn).Send);
        Assert.Equal(2, send.PacerMs, 3);
        Assert.Equal(5, send.LossPercent!.Value, 3);
        Assert.Equal(30, send.RoundTripMs!.Value, 3);
        Assert.Equal("bandwidth", send.QualityLimitation);

        var network = Assert.IsType<NetworkPerf>(report.Network);
        Assert.Equal(20, network.RoundTripMs!.Value, 3);
        Assert.True(network.IsOverTcp);
        Assert.Equal("relay", network.LocalCandidate);
    }

    [Fact]
    public void CounterThatWentBackCountsFromZero()
    {
        var sampler = new MediaPerfSampler();
        sampler.Read(Readings(2), NoCodecs);
        Thread.Sleep(5);

        // A stream that restarted reports the counters of one step
        var report = Assert.IsType<MediaPerfReport>(sampler.Read(Readings(1), NoCodecs));

        Assert.Equal(90, report.Find(Screen.UserId, MediaSourceType.Screen)!.Receive!.Packets);
    }

    [Fact]
    public void DecoderOfLibWebRTCIsReported()
    {
        var sampler = new MediaPerfSampler();
        sampler.Read(Readings(0, "MediaCodec"), NoCodecs);
        Thread.Sleep(5);

        var receive = sampler.Read(Readings(1, "MediaCodec"), NoCodecs)!
            .Find(Screen.UserId, MediaSourceType.Screen)!.Receive!;

        Assert.Equal("MediaCodec", receive.DecoderImplementation);
        Assert.Equal(6, receive.DecodeMs!.Value, 3);
    }

    // The objects of a reading after the given number of steps of the counters
    private static List<StatsEntry> Readings(int step, string decoder = "FlowRatePassthrough") =>
    [
        Entry("inbound-rtp", "IT", new()
        {
            ["ssrc"] = ScreenSsrc, ["packetsReceived"] = 90 * step, ["packetsLost"] = 10 * step,
            ["jitterBufferDelay"] = 3.0 * step, ["jitterBufferEmittedCount"] = 60 * step,
            ["totalProcessingDelay"] = 1.2 * step, ["framesDecoded"] = 30 * step, ["totalDecodeTime"] = 0.18 * step,
            ["freezeCount"] = step, ["totalFreezesDuration"] = 0.25 * step
        }, new() { ["kind"] = "video", ["decoderImplementation"] = decoder }),
        Entry("outbound-rtp", "OT", new()
        {
            ["ssrc"] = OurScreenSsrc, ["packetsSent"] = 100 * step, ["totalPacketSendDelay"] = 0.2 * step
        }, new() { ["kind"] = "video", ["qualityLimitationReason"] = "bandwidth" }),
        Entry("remote-inbound-rtp", "RI", new()
        {
            ["ssrc"] = OurScreenSsrc, ["fractionLost"] = 0.05, ["roundTripTime"] = 0.03
        }, []),
        Entry("transport", "T", [], new() { ["selectedCandidatePairId"] = "CP" }),
        Entry("candidate-pair", "CP", new() { ["currentRoundTripTime"] = 0.02 },
            new() { ["localCandidateId"] = "L", ["remoteCandidateId"] = "R" }),
        Entry("local-candidate", "L", [], new() { ["protocol"] = "tcp", ["candidateType"] = "relay" }),
        Entry("remote-candidate", "R", [], new() { ["protocol"] = "udp", ["candidateType"] = "host" })
    ];

    private static StatsEntry Entry(string type, string id, Dictionary<string, double> numbers,
        Dictionary<string, string> texts) => new(type, id, numbers, texts);
}
