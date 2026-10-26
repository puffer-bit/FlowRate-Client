using Client.Core.Sessions;
using Shared.Enums;
using Shared.Permissions;

namespace Client.Core.Coordinator;

/// <summary>
/// What the current user may do on the coordinator, after the roles it has and the roles the user holds.
/// Mirrors the checks of the server, so that the interface offers only what the server allows; the server
/// still decides.
/// </summary>
public sealed class CoordinatorPermissions
{
    /// <summary>Before the roles are known: nothing is allowed.</summary>
    public static CoordinatorPermissions None { get; } = new([], []);

    /// <summary>All roles, lowest first.</summary>
    public IReadOnlyList<RoleModel> Roles { get; }

    /// <summary>The roles of the current user, without the everyone role.</summary>
    public IReadOnlyCollection<int> RoleIds { get; }

    public Permission Granted { get; }

    /// <summary>The highest position among the roles of the current user; 0 for the everyone role alone.</summary>
    public int HighestPosition { get; }

    public CoordinatorPermissions(IReadOnlyList<RoleModel> roles, IReadOnlyCollection<int> roleIds)
    {
        Roles = roles.OrderBy(role => role.Position).ToList();
        RoleIds = roleIds;
        Granted = PermissionsOf(roleIds);
        HighestPosition = HighestPositionOf(roleIds);
    }

    public RoleModel? Everyone => Roles.FirstOrDefault(role => role.IsEveryone);

    public bool Has(Permission permission) => (Granted & permission) == permission;

    /// <summary>What the current user may do in the session, after its overrides.</summary>
    public Permission InSession(SessionModel session) =>
        Everyone is { } everyone
            ? SessionPermissions.Resolve(Granted, session.PermissionOverrides, everyone.Id, RoleIds)
            : Granted;

    public bool HasIn(SessionModel session, Permission permission) =>
        (InSession(session) & permission) == permission;

    /// <summary>
    /// Whether the current user may change the override of the role in a session: a role below its own, and only
    /// permissions it has.
    /// </summary>
    public bool CanOverride(RoleModel role, Permission allow, Permission deny) =>
        Has(Permission.ManageSessions | Permission.ManageRoles) && role.Position < HighestPosition
                                                                && CanGrant(allow | deny);

    public Permission PermissionsOf(IEnumerable<int> roleIds)
    {
        var ids = roleIds.ToHashSet();
        var permissions = Roles.Where(role => role.IsEveryone || ids.Contains(role.Id))
            .Aggregate(Permission.None, (all, role) => all | role.Permissions);
        return permissions.HasFlag(Permission.Administrator) ? Permission.All : permissions;
    }

    public int HighestPositionOf(IEnumerable<int> roleIds)
    {
        var ids = roleIds.ToHashSet();
        return Roles.Where(role => ids.Contains(role.Id)).Select(role => role.Position).DefaultIfEmpty(0).Max();
    }

    /// <summary>Whether the current user ranks strictly above a user with these roles.</summary>
    public bool Outranks(IEnumerable<int> targetRoleIds) => HighestPosition > HighestPositionOf(targetRoleIds);

    /// <summary>Whether the current user may change, move, give or take the role.</summary>
    public bool CanManage(RoleModel role) => Has(Permission.ManageRoles) && role.Position < HighestPosition;

    /// <summary>Whether the current user may give or take the role of another user with these roles.</summary>
    public bool CanAssign(RoleModel role, bool isSelf, IEnumerable<int> targetRoleIds) =>
        !role.IsEveryone && CanManage(role) && (isSelf || Outranks(targetRoleIds));

    /// <summary>A role cannot be given permissions its editor does not have.</summary>
    public bool CanGrant(Permission permissions) => (permissions & ~Granted) == 0;

    /// <summary>
    /// Whether the current user may do something in a hybrid session: by role, or as its host when hosts may
    /// do it (managing, kicking, banning and approving).
    /// </summary>
    public bool CanInSession(Permission permission, bool isHost) =>
        Has(permission) || (isHost && (permission & ~HostPermissions) == 0);

    /// <summary>
    /// Whether the current user may act on another user in a hybrid session, as kick: a role acts only on users
    /// below it, a host also on users of the same rank. Unknown target roles count as the everyone role alone.
    /// </summary>
    public bool CanInSessionOn(Permission permission, bool isHost, IEnumerable<int> targetRoleIds)
    {
        var target = HighestPositionOf(targetRoleIds);
        return (Has(permission) && HighestPosition > target)
               || (isHost && (permission & ~HostPermissions) == 0 && HighestPosition >= target);
    }

    /// <summary>What the host of a hybrid session may do in it, whatever its roles.</summary>
    public const Permission HostPermissions =
        Permission.ManageSessions | Permission.KickMembers | Permission.BanMembers | Permission.ApproveMembers;
}
