using Client.Core.Sessions.Hybrid;
using Client.ViewModels.Sessions.Hybrid;
using Shared.Models.Media;
using Xunit;

namespace Client.Application.Tests;

public class PeerStatsTests
{
    [Fact]
    public void ReadingUsesWeightedLossAndFirstActiveVideoJitter()
    {
        var service = new PeerStatsService();
        var first = service.Get(1, 1);
        var second = service.Get(1, 2);
        first.ShowHistory();
        second.ShowHistory();

        service.Add(1, Sample(
            Stream(1, 95, 5, 10, MediaType.Audio),
            Stream(2, 90, 10, 20, MediaType.Audio),
            Stream(2, 0, 0, 999),
            Stream(2, 90, 10, 30),
            Stream(2, 90, 10, 40)));

        Assert.Equal(0, first.LossHistory.Single()!.Value, 6);
        Assert.Equal(5, second.LossHistory.Single()!.Value, 6);
        Assert.Equal(10d, first.JitterHistory.Single());
        Assert.Equal(30d, second.JitterHistory.Single());
    }

    [Fact]
    public void SilenceAddsGapsWithoutUpdatingOtherSessions()
    {
        var service = new PeerStatsService();
        var peer = service.Get(1, 1);
        var otherSession = service.Get(2, 1);
        peer.ShowHistory();
        otherSession.ShowHistory();

        service.Add(1, Sample(Stream(1, 100, 0, 15)));
        service.Add(1, new TransportStatsSample(null, 0, 0));

        Assert.Equal(new double?[] { 0, null }, peer.LossHistory);
        Assert.Equal(new double?[] { 15, null }, peer.JitterHistory);
        Assert.Equal(ConnectionQuality.Unknown, peer.Quality);
        Assert.Empty(otherSession.LossHistory);
    }

    [Fact]
    public void HiddenHistoryKeepsLatestReadingsWithoutPublishingSnapshots()
    {
        var peer = new PeerStatsVM();
        var historyNotifications = 0;
        peer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PeerStatsVM.LossHistory) or nameof(PeerStatsVM.JitterHistory))
                historyNotifications++;
        };

        for (var i = 0; i < HybridSessionStatsVM.HistoryLength + 5; i++)
            peer.Add(i, i + 1);

        Assert.Empty(peer.LossHistory);
        Assert.Empty(peer.JitterHistory);
        Assert.Equal(0, historyNotifications);

        peer.ShowHistory();

        Assert.Equal(HybridSessionStatsVM.HistoryLength, peer.LossHistory.Count);
        Assert.Equal(5d, peer.LossHistory[0]);
        Assert.Equal(65d, peer.JitterHistory[^1]);
        Assert.Equal(2, historyNotifications);

        peer.HideHistory();
        var snapshot = peer.LossHistory;
        peer.Add(null, null);
        Assert.Same(snapshot, peer.LossHistory);

        peer.ShowHistory();
        Assert.Null(peer.LossHistory[^1]);
        Assert.Null(peer.JitterHistory[^1]);
        Assert.Equal(4, historyNotifications);
    }

    [Fact]
    public void HistoryUpdatesUntilLastViewerCloses()
    {
        var peer = new PeerStatsVM();
        peer.ShowHistory();
        peer.ShowHistory();
        peer.HideHistory();
        peer.Add(1, 2);
        Assert.Equal(1d, peer.LossHistory.Single());

        peer.HideHistory();
        var snapshot = peer.LossHistory;
        peer.Add(3, 4);
        Assert.Same(snapshot, peer.LossHistory);

        peer.ShowHistory();
        Assert.Equal(new double?[] { 1, 3 }, peer.LossHistory);
    }

    [Fact]
    public void ClearingSessionDiscardsItsPeerHistory()
    {
        var service = new PeerStatsService();
        var peer = service.Get(1, 1);
        var otherSession = service.Get(2, 1);
        service.Add(1, Sample(Stream(1, 100, 0, 15)));

        service.Clear(1);

        Assert.NotSame(peer, service.Get(1, 1));
        Assert.Same(otherSession, service.Get(2, 1));
    }

    private static TransportStatsSample Sample(params InboundStreamSample[] streams) => new(null, 0, 0, streams);

    private static InboundStreamSample Stream(int userId, long packets, double loss, double jitter,
        MediaType type = MediaType.Video) => new(userId, MediaSourceType.Screen, type, packets, loss, jitter, 0);
}
