using Client.Core.Sessions.Hybrid;
using Shared.Models.Media;
using Xunit;

namespace Client.Application.Tests;

public class TransportStatsSampleTests
{
    [Fact]
    public void LossEverySenderSharesIsOurs()
    {
        var sample = Sample(Stream(1, 95, 5), Stream(2, 90, 10));

        Assert.Equal(5, sample.OwnInboundLossPercent, 3);
        Assert.Equal(0, sample.SenderLossPercent(1)!.Value, 3);
        Assert.Equal(5, sample.SenderLossPercent(2)!.Value, 3);
    }

    [Fact]
    public void ASingleSenderCannotBeToldApartFromUs()
    {
        var sample = Sample(Stream(1, 90, 10));

        Assert.Equal(10, sample.OwnInboundLossPercent, 3);
        Assert.Equal(10, sample.SenderLossPercent(1)!.Value, 3);
    }

    [Fact]
    public void TheStreamsOfAUserCountByTheirPackets()
    {
        // 10 of 100 video packets and none of 100 voice packets lost
        var sample = Sample(Stream(1, 90, 10), Stream(1, 100, 0, MediaType.Audio));

        Assert.Equal(100.0 * 10 / 200, sample.LossByUser()[1].LossPercent, 3);
    }

    [Fact]
    public void ASenderOfAFewPacketsDoesNotDecideOurLoss()
    {
        var sample = Sample(Stream(1, 90, 10), Stream(2, 5, 0, MediaType.Audio));

        Assert.Equal(10, sample.OwnInboundLossPercent, 3);
        Assert.Equal(ConnectionQuality.Poor, sample.Quality);
    }

    [Fact]
    public void AUserWithoutStreamsHasNoLoss() => Assert.Null(Sample(Stream(1, 90, 10)).SenderLossPercent(2));

    private static TransportStatsSample Sample(params InboundStreamSample[] streams) =>
        new(20, 0, 0, streams);

    // The share lost of what was sent, as the transports report it
    private static InboundStreamSample Stream(int userId, long received, long lost, MediaType type = MediaType.Video) =>
        new(userId, MediaSourceType.Screen, type, received, 100.0 * lost / (received + lost), 0, 0);
}
