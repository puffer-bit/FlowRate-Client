using Shared.Enums.Coordinator;
using Client.Core.Coordinator;

namespace Client.Application.Coordinator;

public partial class CoordinatorSession
{
    // Replaced whole, read from any thread
    private IReadOnlyList<RoleModel> _roles = [];

    public CoordinatorPermissions Permissions { get; private set; } = CoordinatorPermissions.None;

    public event Action<CoordinatorPermissions>? PermissionsChanged;

    public IReadOnlyList<RoleModel> Roles => _roles;

    public Task<(CoordinatorRoleResult Result, IReadOnlyList<RoleModel>? Roles)> GetRoles() =>
        SendAsync(_gateway.GetRoles, CoordinatorRoleResult.Rejected, CoordinatorRoleResult.TimedOut);

    public Task<(CoordinatorRoleResult Result, RoleModel? Role)> CreateRole(RoleChange change) =>
        SendAsync(() => _gateway.CreateRole(change), CoordinatorRoleResult.Rejected, CoordinatorRoleResult.TimedOut);

    public Task<(CoordinatorRoleResult Result, RoleModel? Role)> UpdateRole(int roleId, RoleChange change,
        long expectedRevision) =>
        SendAsync(() => _gateway.UpdateRole(roleId, change, expectedRevision),
            CoordinatorRoleResult.Rejected, CoordinatorRoleResult.TimedOut);

    public Task<CoordinatorRoleResult> DeleteRole(int roleId) =>
        SendAsync(() => _gateway.DeleteRole(roleId), CoordinatorRoleResult.Rejected, CoordinatorRoleResult.TimedOut);

    public Task<(CoordinatorRoleResult Result, IReadOnlyList<RoleModel>? Roles)> ReorderRoles(
        IReadOnlyList<int> roleIds) =>
        SendAsync(() => _gateway.ReorderRoles(roleIds), CoordinatorRoleResult.Rejected, CoordinatorRoleResult.TimedOut);

    public Task<(CoordinatorRoleResult Result, IReadOnlyList<int>? RoleIds)> SetUserRole(int userId, int roleId,
        bool has) =>
        SendAsync(() => _gateway.SetUserRole(userId, roleId, has),
            CoordinatorRoleResult.Rejected, CoordinatorRoleResult.TimedOut);

    /// <summary>Loads the roles on login; without them nothing is allowed, which the server agrees with.</summary>
    private async Task LoadRolesAsync(CancellationToken cancellationToken)
    {
        var (result, roles) = await _gateway.GetRoles().WaitAsync(cancellationToken);
        if (roles == null)
            LogRolesNotLoaded(LogName, result);

        _roles = roles ?? [];
    }

    private void OnRolesChanged(IReadOnlyList<RoleModel> roles)
    {
        _roles = roles;
        UpdatePermissions();
    }

    /// <summary>Follows the roles and the roles of the current user; raised only when something changed.</summary>
    private void UpdatePermissions()
    {
        var permissions = new CoordinatorPermissions(_roles, _currentUser?.RoleIds ?? []);
        var previous = Permissions;
        Permissions = permissions;

        if (!previous.Roles.SequenceEqual(permissions.Roles) || !previous.RoleIds.SequenceEqual(permissions.RoleIds))
            PermissionsChanged?.Invoke(permissions);
    }
}
