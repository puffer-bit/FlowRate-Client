using Client.Core.Coordinator;
using Client.Core.Sessions.Hybrid;
using Client.Core.Users;
using Client.ViewModels.Sessions.Hybrid;
using Shared.Enums;
using Shared.Enums.WebRTC;
using Shared.Permissions;
using Xunit;

namespace Client.Application.Tests;

public class ParticipantVoicePermissionsTests
{
    [Fact]
    public void Joining_HidesUnconfirmedMute_AndAlwaysShowsThePermissionRestriction()
    {
        var peer = new HybridSessionParticipantVM(new PeerModel
        {
            UserId = 2, State = HybridSessionPeerState.Connecting, IsMicrophoneMuted = true
        }, false);
        Assert.False(peer.ShowsMicrophoneOff);
        peer.UpdatePeerStatus(HybridSessionPeerState.Connected);
        Assert.False(peer.ShowsMicrophoneOff);
        peer.UpdateMicrophoneMuted(true);
        Assert.True(peer.ShowsMicrophoneOff);

        peer.UpdatePeerStatus(HybridSessionPeerState.Connecting);
        peer.IsVoiceForbidden = true;
        Assert.True(peer.ShowsMicrophoneOff);
        peer.IsVoiceForbidden = false;
        Assert.True(peer.ShowsMicrophoneOff);
    }

    [Fact]
    public void Rejoin_RefreshesAnExistingParticipantFromTheSnapshot()
    {
        var participants = new HybridSessionParticipantsListVM(1, _ => { }, _ => { }, _ => { });
        participants.AddPeerToSession(new PeerModel { UserId = 2, IsScreenShareActive = true });
        var original = Assert.Single(participants.Participants);
        participants.AddPeerToSession(new PeerModel
        {
            UserId = 2, State = HybridSessionPeerState.Connecting, IsMicrophoneMuted = true
        });

        Assert.Same(original, Assert.Single(participants.Participants));
        Assert.True(original.PeerData.IsMicrophoneMuted);
        Assert.False(original.ShowsMicrophoneOff);
        Assert.False(original.IsSharingScreen);
    }
    private static readonly RoleModel Everyone = new(1, "@everyone", null, 0, Permission.Speak, true, 0);
    private static readonly RoleModel Restricted = new(2, "Restricted", null, 1, Permission.None, false, 0);
    private static readonly RoleModel Admin = new(3, "Admin", null, 2, Permission.Administrator, false, 0);
    private static readonly HybridSessionModel Room = new()
    {
        Id = 1, Name = "Room", Capacity = 10, SessionType = SessionType.Hybrid,
        EngineType = WebRTCEngine.Mediasoup, HostId = 1,
        PermissionOverrides = [new PermissionOverride(2, Permission.None, Permission.Speak)]
    };

    private static UserModel User(params int[] roles) => new()
    {
        Id = 2, CoordinatorInstanceId = "server", Username = "Peer", RoleIds = roles
    };

    [Fact]
    public void TargetPermissions_UseTheirRolesAndSessionOverrides_InsteadOfTheViewerPermissions()
    {
        var viewer = new CoordinatorPermissions([Everyone, Restricted, Admin], [3]);

        Assert.True(viewer.HasIn(Room, Permission.Speak));
        Assert.False(viewer.InSession(Room, new[] { 2 }).HasFlag(Permission.Speak));
        Assert.True(viewer.InSession(Room, Array.Empty<int>()).HasFlag(Permission.Speak));
        Assert.True(viewer.InSession(Room, new[] { 2, 3 }).HasFlag(Permission.Speak));
    }

    [Fact]
    public void ParticipantRestriction_FollowsRoleAssignmentsAndSessionOverrides()
    {
        var permissions = new CoordinatorPermissions([Everyone, Restricted, Admin], [3]);
        var participants = new HybridSessionParticipantsListVM(1, _ => { }, _ => { }, _ => { });
        participants.ApplyAccess(permissions, true, Room);
        participants.AddPeerToSession(new PeerModel { UserId = 2 });
        var peer = Assert.Single(participants.Participants);
        Assert.False(peer.IsVoiceForbidden);

        participants.UpdateUserModel(2, User(2));
        Assert.True(peer.IsVoiceForbidden);

        participants.UpdateUserModel(2, User());
        Assert.False(peer.IsVoiceForbidden);

        participants.UpdateUserModel(2, User(2));
        Assert.True(peer.IsVoiceForbidden);
        var unrestrictedRoom = new HybridSessionModel
        {
            Id = 1, Name = "Room", Capacity = 10, SessionType = SessionType.Hybrid,
            EngineType = WebRTCEngine.Mediasoup, HostId = 1
        };
        participants.ApplyAccess(permissions, true, unrestrictedRoom);
        Assert.False(peer.IsVoiceForbidden);
    }

    [Fact]
    public void PermissionRestore_KeepsTheMutedIconUntilVoiceActuallyStarts()
    {
        var permissions = new CoordinatorPermissions([Everyone, Restricted, Admin], [3]);
        var participants = new HybridSessionParticipantsListVM(1, _ => { }, _ => { }, _ => { });
        participants.ApplyAccess(permissions, true, Room);
        participants.AddPeerToSession(new PeerModel { UserId = 2, IsMicrophoneMuted = true });
        participants.UpdateUserModel(2, User(2));
        var peer = Assert.Single(participants.Participants);
        Assert.Equal("ForbiddenMicrophoneIcon", peer.MicrophoneIconKey);

        participants.UpdateUserModel(2, User());

        Assert.False(peer.IsVoiceForbidden);
        Assert.True(peer.ShowsMicrophoneOff);
        Assert.Equal("CrossedMicrophoneIcon", peer.MicrophoneIconKey);

        participants.UpdateMicrophoneMuted(2, false);
        Assert.False(peer.ShowsMicrophoneOff);
        participants.UpdateMicrophoneMuted(2, true);
        Assert.True(peer.ShowsMicrophoneOff);
    }
}
