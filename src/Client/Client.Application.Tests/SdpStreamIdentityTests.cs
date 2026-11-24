using Client.Core.Sessions.Hybrid;
using Client.WebRTC.Sdp;
using Xunit;

namespace Client.Application.Tests;

public class SdpStreamIdentityTests
{
    private static readonly TransportParameters Transport = new("127.0.0.1", 9,
        new DtlsParameters("auto", [new DtlsFingerprint("sha-256", "00:01")]),
        new IceParameters("test", "test-password", []));

    [Fact]
    public void PlaceholderKeepsItsExplicitStreamWhenFirstParticipantArrives()
    {
        var placeholder = Section("0");
        var initial = SdpBuilder.Build(Transport, [placeholder]);
        var joined = SdpBuilder.Build(Transport, [placeholder, Section("1") with { SSRC = 1234 }]);
        var left = SdpBuilder.Build(Transport,
            [placeholder, Section("1") with { Direction = SdpDirection.Inactive }]);

        const string identity = "a=msid:flowrate-0 flowrate-0\r\n";
        Assert.Contains(identity, initial);
        Assert.Contains(identity, joined);
        Assert.Contains(identity, left);
        Assert.DoesNotContain("a=ssrc:", initial);
        Assert.Contains("a=msid:flowrate-1 flowrate-1\r\n", joined);
        Assert.Contains("a=ssrc:1234 cname:sfu\r\n", joined);
        Assert.DoesNotContain("a=msid:flowrate-1", left);
    }

    [Theory]
    [InlineData(SdpDirection.RecvOnly)]
    [InlineData(SdpDirection.Inactive)]
    public void NonSendingSectionDoesNotAnnounceRemoteStream(SdpDirection direction)
    {
        var sdp = SdpBuilder.Build(Transport, [Section("0") with { Direction = direction, SSRC = 1234 }]);

        Assert.DoesNotContain("a=msid:", sdp);
        Assert.DoesNotContain("a=ssrc:", sdp);
    }

    private static SdpMediaSection Section(string mid) => new()
    {
        Mid = mid, Kind = "audio", Direction = SdpDirection.SendOnly, Codec = RtpCodec.Opus()
    };
}
