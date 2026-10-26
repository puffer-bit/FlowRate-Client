using Client.Core.Sessions.Hybrid;
using Client.LibWebRTC.Native;
using Client.LibWebRTC.Voice;
using Shared.Models.Media;
using Xunit;

namespace Client.Application.Tests;

public class LibWebRTCStatsTests
{
    private const uint VoiceSsrc = 555;
    private const uint MicrophoneSsrc = 123456789;

    private static readonly InboundTrack Voice = new()
    {
        Id = "in1", UserId = 8, SSRC = VoiceSsrc, SourceType = MediaSourceType.Microphone, MediaType = MediaType.Audio,
        MimeType = "audio/opus", PayloadType = 100, ClockRate = 48000
    };

    [Fact]
    public void ReportIsReadIntoLevelsAndCounters()
    {
        var stats = StatsReader.Read(Report(received: 500, lost: 3, sent: 400, fractionLost: 0.25, overTcp: true));

        // The level of the voice is the one before the playback volume, not the audioLevel of libwebrtc
        Assert.Equal([new VoiceLevel(null, 0.125), new VoiceLevel("2", 16384.0 / short.MaxValue)], stats.Levels);

        var streams = Assert.IsType<VoiceStreamCounters>(stats.Streams);
        Assert.Equal(new InboundCounters(VoiceSsrc, 500, 3, 20), Assert.Single(streams.Inbound));
        Assert.Equal(new OutboundCounters(MicrophoneSsrc, 400, 0.25, 40), Assert.Single(streams.Outbound));
        Assert.True(streams.IsOverTcp);
    }

    [Fact]
    public void UdpPairIsNotOverTcp()
    {
        var streams = StatsReader.Read(Report(received: 1, lost: 0, sent: 1, fractionLost: 0, overTcp: false)).Streams!;

        Assert.False(streams.IsOverTcp);
    }

    [Fact]
    public void SampleCoversWhatHappenedSinceThePreviousOne()
    {
        var sampler = new VoiceStatsSampler();

        // The first reading only sets the counters to count from
        var first = Read(sampler, received: 100, lost: 0, sent: 100, fractionLost: null);
        Assert.Equal(0, first.InboundLossPercent);
        Assert.Empty(first.Inbound!);
        Assert.Null(first.RoundTripMs);

        for (var reading = 1; reading < VoiceStatsSampler.ReadingsPerSample; reading++)
            Assert.Null(sampler.Read(Counters(150, 5, 150, 0.5), FindVoice, IsMicrophone));

        var sample = Read(sampler, received: 190, lost: 10, sent: 200, fractionLost: 0.05);

        Assert.Equal(10, sample.InboundLossPercent, 3);
        Assert.Equal(5, sample.OutboundLossPercent, 3);
        Assert.Equal(40, sample.RoundTripMs);

        var voice = Assert.Single(sample.Inbound!);
        Assert.Equal(Voice.UserId, voice.UserId);
        Assert.Equal(90, voice.Packets);
    }

    [Fact]
    public void StoppedStreamDoesNotReportItsLastLoss()
    {
        var sampler = new VoiceStatsSampler();
        Read(sampler, received: 100, lost: 0, sent: 100, fractionLost: null);

        for (var reading = 1; reading < VoiceStatsSampler.ReadingsPerSample; reading++)
            sampler.Read(Counters(100, 0, 100, 0.5), FindVoice, IsMicrophone);

        // The SFU repeats its last report of a stream we no longer send in
        var sample = Read(sampler, received: 100, lost: 0, sent: 100, fractionLost: 0.5);

        Assert.Equal(0, sample.OutboundLossPercent);
        Assert.Null(sample.RoundTripMs);
    }

    private static TransportStatsSample Read(VoiceStatsSampler sampler, long received, long lost, long sent,
        double? fractionLost) =>
        Assert.IsType<TransportStatsSample>(sampler.Read(Counters(received, lost, sent, fractionLost), FindVoice, IsMicrophone));

    private static VoiceStreamCounters Counters(long received, long lost, long sent, double? fractionLost) =>
        new([new InboundCounters(VoiceSsrc, received, lost, 20)],
            [new OutboundCounters(MicrophoneSsrc, sent, fractionLost, fractionLost == null ? null : 40)], false);

    private static InboundTrack? FindVoice(uint ssrc) => ssrc == VoiceSsrc ? Voice : null;

    private static bool IsMicrophone(uint ssrc) => ssrc == MicrophoneSsrc;

    // A report as libwebrtc writes it, with the 64-bit counters as strings in places
    private static string Report(long received, long lost, long sent, double fractionLost, bool overTcp) => $$"""
        [
          {"type":"inbound-rtp","id":"IT01A555","kind":"audio","mid":"2","ssrc":{{VoiceSsrc}},"packetsReceived":"{{received}}","packetsLost":{{lost}},"jitter":0.02,"audioLevel":0.01},
          {"type":"inbound-rtp","id":"IT01V9","kind":"video","mid":"3","ssrc":9,"packetsReceived":7,"packetsLost":0,"jitter":0},
          {"type":"media-source","id":"SA1","kind":"audio","audioLevel":0.125},
          {"type":"outbound-rtp","id":"OT01A1","kind":"audio","ssrc":{{MicrophoneSsrc}},"packetsSent":{{sent}}},
          {"type":"remote-inbound-rtp","id":"RI01A1","kind":"audio","ssrc":{{MicrophoneSsrc}},"fractionLost":{{fractionLost.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"roundTripTime":0.04},
          {"type":"transport","id":"T01","selectedCandidatePairId":"CP1"},
          {"type":"candidate-pair","id":"CP1","localCandidateId":"L1","remoteCandidateId":"R1"},
          {"type":"local-candidate","id":"L1","protocol":"{{(overTcp ? "tcp" : "udp")}}"},
          {"type":"remote-candidate","id":"R1","protocol":"{{(overTcp ? "tcp" : "udp")}}"},
          {"type":"flowrate-voice-level","mid":"2","peak":16384}
        ]
        """;
}
