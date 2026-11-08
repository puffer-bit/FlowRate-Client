using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Client.WebRTC.Sdp;
using Shared.Models.Media;
using Xunit;

namespace Client.Application.Tests;

public class VideoCodecNegotiationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Av1OfferKeepsTheServersPayloadAndLossRecovery(bool nack)
    {
        var codec = RtpCodec.FromInbound(new InboundTrack
        {
            Id = "av1", UserId = 2, SSRC = 1234, MediaType = MediaType.Video,
            SourceType = MediaSourceType.Screen, MimeType = "video/AV1", PayloadType = 104,
            ClockRate = 90000, IsNackEnabled = nack
        });
        var transport = new TransportParameters("127.0.0.1", 9,
            new DtlsParameters("auto", [new DtlsFingerprint("sha-256", "00:01")]),
            new IceParameters("test", "test-password", []));
        var sdp = SdpBuilder.Build(transport,
        [
            new SdpMediaSection
            {
                Mid = "video", Kind = "video", Direction = SdpDirection.SendOnly, Codec = codec, SSRC = 1234
            }
        ]);

        Assert.Contains("a=rtpmap:104 AV1/90000\r\n", sdp);
        Assert.Contains("a=rtcp-fb:104 nack pli\r\n", sdp);
        Assert.Equal(nack, sdp.Contains("a=rtcp-fb:104 nack\r\n"));
        Assert.DoesNotContain("profile-level-id", sdp);
        Assert.Equal(VideoCodec.AV1, RtpCodec.VideoCodecOf(codec.EncodingName));
    }

    [Theory]
    [InlineData("video/H265")]
    [InlineData("video/unknown")]
    [InlineData("")]
    public void UnsupportedVideoIsNotTreatedAsH264(string mimeType) =>
        Assert.Null(RtpCodec.VideoCodecOf(mimeType));
}
