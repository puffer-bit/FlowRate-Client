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
    [Fact]
    public void Joining_DoesNotFlashMuteWhileTransportConnects_ButConfirmedMuteIsVisible()
    {
        var peer = CreatePeer(false);
        peer.UpdatePeerModel(new PeerModel
        {
            UserId = 1, State = Shared.Enums.HybridSessionPeerState.Connecting, IsMicrophoneMuted = true
        });
        Assert.False(peer.ShowsMicrophoneOff);

        peer.UpdatePeerStatus(Shared.Enums.HybridSessionPeerState.Connected);
        Assert.False(peer.ShowsMicrophoneOff);

        peer.UpdateMicrophoneMuted(true);
        Assert.True(peer.ShowsMicrophoneOff);
        peer.UpdateMicrophoneMuted(false);
        Assert.False(peer.ShowsMicrophoneOff);
    }

    [Fact]
    public void RepeatedMediaNotifications_DoNotRedrawThePeer()
    {
        var peer = CreatePeer(false);
        peer.UpdateMicrophoneMuted(true);
        peer.UpdateSoundMuted(true);
        peer.UpdateAfk(true, "away");
        peer.UpdateScreenShare(true);
        var notifications = 0;
        peer.PropertyChanged += (_, _) => notifications++;

        peer.UpdateMicrophoneMuted(true);
        peer.UpdateSoundMuted(true);
        peer.UpdateAfk(true, "away");
        peer.UpdateScreenShare(true);

        Assert.Equal(0, notifications);
    }

    [Fact]
    public void LeavingRow_KeepsItsMediaAppearanceUntilRemoved()
    {
        var peer = CreatePeer(false);
        peer.UpdateScreenShare(true);
        peer.IsLeaving = true;

        peer.UpdateMicrophoneMuted(true);
        peer.UpdateSoundMuted(true);
        peer.UpdateAfk(true, "late event");
        peer.UpdateScreenShare(false);

        Assert.False(peer.ShowsMicrophoneOff);
        Assert.False(peer.ShowsSoundOff);
        Assert.False(peer.IsAfk);
        Assert.True(peer.IsSharingScreen);
    }

    [Fact]
    public void RejoinSnapshot_ReplacesStaleMediaStateWithoutReplacingTheRow()
    {
        var peer = CreatePeer(false);
        peer.UpdateAfk(true, "old state");
        peer.UpdateScreenShare(true);

        peer.UpdatePeerModel(new PeerModel
        {
            UserId = 1, State = Shared.Enums.HybridSessionPeerState.Connecting, IsMicrophoneMuted = true
        });

        Assert.True(peer.IsMicrophoneMuted);
        Assert.False(peer.ShowsMicrophoneOff);
        Assert.False(peer.IsAfk);
        Assert.False(peer.IsSharingScreen);
    }
    [Fact]
    public void ForbiddenMicrophone_IsVisibleToAnotherUser_EvenWhileThePeerIsAfk()
    {
        var peer = CreatePeer(false);
        peer.UpdateAfk(true, null);
        peer.UpdateMicrophoneMuted(false);
        peer.IsVoiceForbidden = true;

        Assert.True(peer.ShowsMicrophoneOff);
        Assert.Equal("ForbiddenMicrophoneIcon", peer.MicrophoneIconKey);

        peer.IsVoiceForbidden = false;
        Assert.False(peer.ShowsMicrophoneOff);
        peer.UpdateAfk(false, null);
        peer.UpdateMicrophoneMuted(true);
        Assert.True(peer.ShowsMicrophoneOff);
        Assert.Equal("CrossedMicrophoneIcon", peer.MicrophoneIconKey);
    }

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
