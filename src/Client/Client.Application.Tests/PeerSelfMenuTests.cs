using Client.Application.Coordinator.Users;
using Client.Application.Tests.Fakes;
using Client.Core.Sessions.Hybrid;
using Client.ViewModels.Sessions.Hybrid;
using Client.ViewModels.Sessions.List;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;
using Xunit;

namespace Client.Application.Tests;

public class PeerSelfMenuTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void ControlsAreAvailableOnlyForOurselvesInTheEnteredSession(bool isSelf, bool isEntered, bool available)
    {
        var peer = CreatePeer(isSelf);
        var controls = CreateControls();
        peer.IsInEnteredSession = isEntered;
        peer.SetSessionControls(controls);

        Assert.Same(available ? controls : null, peer.SelfSession);
    }

    [Fact]
    public void LeavingAndReplacingTheSessionUpdatesTheMenu()
    {
        var peer = CreatePeer(true);
        var first = CreateControls();
        var next = CreateControls();
        var notifications = 0;
        peer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PeerVM.SelfSession))
                notifications++;
        };

        peer.SetSessionControls(first);
        peer.IsInEnteredSession = true;
        Assert.Same(first, peer.SelfSession);
        peer.IsInEnteredSession = false;
        Assert.Null(peer.SelfSession);
        peer.SetSessionControls(next);
        peer.IsInEnteredSession = true;
        Assert.Same(next, peer.SelfSession);
        peer.SetSessionControls(null);
        Assert.Null(peer.SelfSession);
        Assert.Equal(6, notifications);
    }

    private static PeerVM CreatePeer(bool isSelf) => new(
        new PeerModel { UserId = 1 }, isSelf,
        new PeerPlaybackService(new MemoryRememberedUsers(), NullLogger<PeerPlaybackService>.Instance),
        ReactiveCommand.Create<int>(_ => { }), _ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });

    private static HybridSessionStatusBarVM CreateControls()
    {
        var action = ReactiveCommand.Create(() => { });
        return new HybridSessionStatusBarVM("Session", ReactiveCommand.Create<bool>(_ => { }),
            action, action, action, action, action, action, action);
    }
}
