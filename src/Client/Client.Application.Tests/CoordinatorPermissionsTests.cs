using Client.Core.Coordinator;
using Shared.Enums;
using Xunit;

namespace Client.Application.Tests;

public class CoordinatorPermissionsTests
{
    private const int EveryoneId = 1;
    private const int MemberId = 2;
    private const int ModeratorId = 3;
    private const int AdminId = 4;

    private static readonly IReadOnlyList<RoleModel> Roles =
    [
        new(EveryoneId, "@everyone", null, 0, Permission.CreateSessions, true, 0),
        new(MemberId, "Member", null, 1, Permission.ManageMessages, false, 0),
        new(ModeratorId, "Moderator", 0xFF0000, 2, Permission.KickMembers | Permission.ManageRoles, false, 0),
        new(AdminId, "Admin", null, 3, Permission.Administrator, false, 0)
    ];

    private static CoordinatorPermissions As(params int[] roleIds) => new(Roles, roleIds);

    [Fact]
    public void None_AllowsNothing()
    {
        Assert.False(CoordinatorPermissions.None.Has(Permission.CreateSessions));
        Assert.Empty(CoordinatorPermissions.None.Roles);
    }

    [Fact]
    public void Everyone_GivesItsPermissionsToAll()
    {
        Assert.True(As().Has(Permission.CreateSessions));
        Assert.False(As().Has(Permission.ManageMessages));
    }

    [Fact]
    public void Administrator_HasAll()
    {
        Assert.Equal(Permission.All, As(AdminId).Granted);
    }

    [Fact]
    public void Roles_AddUp()
    {
        Assert.True(As(MemberId, ModeratorId).Has(Permission.ManageMessages | Permission.KickMembers));
    }

    [Fact]
    public void CanManage_OnlyRolesBelowTheHighest_WithManageRoles()
    {
        var moderator = As(ModeratorId);

        Assert.True(moderator.CanManage(Roles[1]));
        Assert.True(moderator.CanManage(Roles[0]));
        Assert.False(moderator.CanManage(Roles[2]));
        Assert.False(moderator.CanManage(Roles[3]));
        Assert.False(As(MemberId).CanManage(Roles[0]));
    }

    [Fact]
    public void CanAssign_NeedsTheTargetBelow_UnlessItIsOneself()
    {
        var moderator = As(ModeratorId);

        Assert.True(moderator.CanAssign(Roles[1], isSelf: false, []));
        Assert.False(moderator.CanAssign(Roles[1], isSelf: false, [ModeratorId]));
        Assert.True(moderator.CanAssign(Roles[1], isSelf: true, [ModeratorId]));
        Assert.False(moderator.CanAssign(Roles[0], isSelf: false, []));
    }

    [Fact]
    public void Kick_ByRole_OnlyBelow()
    {
        var moderator = As(ModeratorId);

        Assert.True(moderator.CanInSessionOn(Permission.KickMembers, isHost: false, [MemberId]));
        Assert.False(moderator.CanInSessionOn(Permission.KickMembers, isHost: false, [ModeratorId]));
    }

    [Fact]
    public void Kick_ByHost_AlsoTheSameRank_ButNotHigher()
    {
        var plain = As();

        Assert.True(plain.CanInSessionOn(Permission.KickMembers, isHost: true, []));
        Assert.False(plain.CanInSessionOn(Permission.KickMembers, isHost: true, [MemberId]));
        Assert.False(plain.CanInSessionOn(Permission.KickMembers, isHost: false, []));
    }

    [Fact]
    public void Host_IsNotGivenWhatHostsMayNotDo()
    {
        Assert.True(As().CanInSession(Permission.ManageSessions, isHost: true));
        Assert.False(As().CanInSession(Permission.ManageMessages, isHost: true));
    }
}
