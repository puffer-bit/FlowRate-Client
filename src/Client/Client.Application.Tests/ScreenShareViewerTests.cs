using Client.Api.Mappers;
using Client.Core.Coordinator;
using Client.Application.Sessions.Hybrid;
using Client.Avalonia.Controls.Media;
using Avalonia.Controls;
using Client.Core.Sessions.Hybrid;
using Shared.Models.DataTransferObjects;
using Shared.Models.Media;
using Shared.Models.Notifications.SessionInfo;
using Xunit;

namespace Client.Application.Tests;

public sealed class ScreenShareViewerTests
{
    [Fact]
    public Task AvatarButtonAndDetachedFlyoutFollowAudienceChanges() => TestUiThread.RunAsync(() =>
    {
        var audience = new ScreenViewersButton();
        var button = Assert.IsType<Button>(audience.Content);
        var flyout = Assert.IsType<Flyout>(button.Flyout);
        var contents = Assert.IsType<StackPanel>(flyout.Content);
        var list = Assert.IsType<ItemsControl>(contents.Children.OfType<ScrollViewer>().Single().Content);

        ScreenViewerItem[] initial = [new(9, "Alice", "avatar-a"), new(10, "Bob", null)];
        audience.Viewers = initial;
        Assert.Equal(initial, list.ItemsSource!.Cast<ScreenViewerItem>());
        Assert.True(button.IsVisible);

        var preview = audience.Preview;
        var rows = list.ItemsSource;
        var changes = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        ((System.Collections.Specialized.INotifyCollectionChanged)preview).CollectionChanged +=
            (_, change) => changes.Add(change.Action);
        audience.Viewers = [.. initial, new(11, "Carol", null)];
        Assert.Same(preview, audience.Preview);
        Assert.Same(rows, list.ItemsSource);
        Assert.Same(initial[0], audience.Preview[0]);
        Assert.Equal(new[] { System.Collections.Specialized.NotifyCollectionChangedAction.Add }, changes);
        changes.Clear();
        audience.Viewers = [initial[0], new(11, "Carol", null)];
        Assert.Equal(new[] { System.Collections.Specialized.NotifyCollectionChangedAction.Remove }, changes);
        Assert.Same(initial[0], audience.Preview[0]);

        // The popup is a separate tree; both a renamed viewer and an updated avatar must reach it.
        ScreenViewerItem[] updated = [new(9, "Alicia", "avatar-b")];
        audience.Viewers = updated;
        Assert.Equal(updated, list.ItemsSource!.Cast<ScreenViewerItem>());
        Assert.Equal(updated, audience.Preview);

        audience.Viewers = [];
        Assert.Empty(list.ItemsSource!.Cast<ScreenViewerItem>());
        Assert.False(button.IsVisible);
    });

    [Fact]
    public void PeerSnapshotPreservesWatchedShares()
    {
        var dto = new PeerDTO { UserId = 9, WatchedScreenShares = [12, 14] };
        var peer = dto.ToModel();
        dto.WatchedScreenShares.Clear();
        Assert.Equal(new[] { 12, 14 }, peer.WatchedScreenShares);
    }

    [Fact]
    public void SnapshotAndLiveChangesTrackEverySharerWithoutDuplicatesOrLocalPreviews()
    {
        var audience = new ScreenShareAudience(7);
        audience.LoadSnapshot([
            new PeerModel { UserId = 9, WatchedScreenShares = [12, 14] },
            new PeerModel { UserId = 12, WatchedScreenShares = [12] }
        ]);
        Assert.Equal(new[] { 9 }, audience.GetViewers(12));
        audience.Apply(new PeerScreenShareWatchChangedEvent(7, 9, 12, true));
        audience.Apply(new PeerScreenShareWatchChangedEvent(7, 10, 12, true));
        audience.Apply(new PeerScreenShareWatchChangedEvent(8, 11, 12, true));
        Assert.Equal(new[] { 9, 10 }, audience.GetViewers(12).Order());
        audience.Apply(new PeerScreenShareWatchChangedEvent(7, 9, 12, false));
        Assert.Equal(new[] { 10 }, audience.GetViewers(12));
        Assert.Equal(new[] { 9 }, audience.GetViewers(14));
    }

    [Fact]
    public void LeavingAndStoppingClearViewersAndRestartBeginsEmpty()
    {
        var audience = new ScreenShareAudience(7);
        audience.LoadSnapshot([new PeerModel { UserId = 9, WatchedScreenShares = [12, 14] }]);
        audience.Apply(new PeerTrackStoppedEvent(7, 12, MediaType.Video, MediaSourceType.Screen));
        Assert.Empty(audience.GetViewers(12));
        audience.Apply(new PeerLeftEvent(7, 9));
        Assert.Empty(audience.GetViewers(14));
        audience.Apply(new PeerScreenShareWatchChangedEvent(7, 9, 12, true));
        audience.Apply(new PeerLeftEvent(7, 12));
        Assert.Empty(audience.GetViewers(12));
    }

    [Fact]
    public void EventsReceivedWhileSnapshotLoadsAreReplayedInOrder()
    {
        var audience = new ScreenShareAudience(7);
        audience.Apply(new PeerScreenShareWatchChangedEvent(7, 9, 12, false));
        audience.Apply(new PeerScreenShareWatchChangedEvent(7, 10, 12, true));
        audience.LoadSnapshot([new PeerModel { UserId = 9, WatchedScreenShares = [12] }]);
        Assert.Equal(new[] { 10 }, audience.GetViewers(12));
    }

    [Fact]
    public void RejoiningReplacesThePeersPreviousSubscriptions()
    {
        var audience = new ScreenShareAudience(7);
        audience.LoadSnapshot([new PeerModel { UserId = 9, WatchedScreenShares = [12] }]);
        audience.Apply(new PeerJoinedEvent(7, new PeerModel { UserId = 9, WatchedScreenShares = [14] }));
        Assert.Empty(audience.GetViewers(12));
        Assert.Equal(new[] { 9 }, audience.GetViewers(14));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NotificationPreservesSessionViewerSharerAndWatchState(bool isWatching)
    {
        var result = CoordinatorEventMapper.ToEvent(
            new HybridSessionPeerScreenShareWatchChangedNotification(7, 9, 12, isWatching));

        Assert.Equal(new PeerScreenShareWatchChangedEvent(7, 9, 12, isWatching), result);
        var peerEvent = Assert.IsAssignableFrom<HybridSessionPeerEvent>(result);
        Assert.Equal(9, peerEvent.UserId);
    }
}
