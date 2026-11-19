using Client.Application.Coordinator.Users;
using Client.Application.Tests.Fakes;
using Client.Core.Sessions.Hybrid;
using Client.ViewModels.Sessions.List;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;
using Shared.Enums;
using Shared.Enums.WebRTC;
using Xunit;

namespace Client.Application.Tests;

public class SessionCapacityIndicatorTests
{
    private static HybridSessionModel Model(int? capacity) => new()
    {
        Id = 7, Name = "Room", Capacity = capacity, SessionType = SessionType.Hybrid,
        EngineType = WebRTCEngine.Mediasoup, HostId = 1
    };

    private static HybridSessionVM Create(int? capacity) => new(Model(capacity), 1,
        null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);

    private static PeerVM Peer(int id) => new(new PeerModel { UserId = id }, false,
        new PeerPlaybackService(new MemoryRememberedUsers(), NullLogger<PeerPlaybackService>.Instance),
        ReactiveCommand.Create<int>(_ => { }), _ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });

    [Fact]
    public async Task FullIndicator_TracksMembershipCapacityAndLeavingAnimation()
    {
        var room = Create(2);
        room.Peers.Add(Peer(1));
        Assert.False(room.IsFull);
        var changes = new List<string?>();
        room.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        room.Peers.Add(Peer(2));
        Assert.True(room.IsFull);
        Assert.True(room.HasStatusWarning);
        Assert.True(room.ShowsFullWarning);
        Assert.Contains(nameof(room.ShowsFullWarning), changes);

        room.Model = Model(3);
        Assert.False(room.IsFull);
        room.Model = Model(2);
        Assert.True(room.IsFull);

        var leaving = room.RemovePeerFromSession(2);
        Assert.False(room.IsFull);
        Assert.False(room.HasStatusWarning);
        await leaving;
        Assert.Single(room.Peers);
    }

    [Fact]
    public void LoadError_KeepsItsOwnWarningWhenRoomIsAlsoFull()
    {
        var room = Create(1);
        room.Peers.Add(Peer(1));
        room.IsCorrupted = true;
        Assert.True(room.HasStatusWarning);
        Assert.False(room.ShowsFullWarning);
        room.IsCorrupted = false;
        Assert.True(room.ShowsFullWarning);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(0)]
    public void UnlimitedSession_HasNoFullWarning(int? capacity)
    {
        var room = Create(capacity);
        room.Peers.Add(Peer(1));
        Assert.False(room.IsFull);
        Assert.False(room.HasStatusWarning);
    }
}
