using Client.Application.Ports;
using Client.Core.Coordinator;

namespace Client.Application.Coordinator;

public partial class CoordinatorSession
{
    // Replaced whole, read from any thread
    private IReadOnlyList<RoleModel> _roles = [];

    public CoordinatorPermissions Permissions { get; private set; } = CoordinatorPermissions.None;

    public event Action<CoordinatorPermissions>? PermissionsChanged;

    public IReadOnlyList<RoleModel> Roles => _roles;

    public Task<RequestResult<IReadOnlyList<RoleModel>>> GetRoles() =>
        SendAsync(_gateway.GetRoles);

    public Task<RequestResult<RoleModel>> CreateRole(RoleChange change) =>
        SendAsync(() => _gateway.CreateRole(change));

    public Task<RequestResult<RoleModel>> UpdateRole(int roleId, RoleChange change,
        long expectedRevision) =>
        SendAsync(() => _gateway.UpdateRole(roleId, change, expectedRevision));

    public Task<RequestResult> DeleteRole(int roleId) =>
        SendAsync(() => _gateway.DeleteRole(roleId));

    public Task<RequestResult<IReadOnlyList<RoleModel>>> ReorderRoles(
        IReadOnlyList<int> roleIds) =>
        SendAsync(() => _gateway.ReorderRoles(roleIds));

    public Task<RequestResult<IReadOnlyList<int>>> SetUserRole(int userId, int roleId,
        bool has) =>
        SendAsync(() => _gateway.SetUserRole(userId, roleId, has));

    /// <summary>Loads the roles on login; without them nothing is allowed, which the server agrees with.</summary>
    private async Task LoadRolesAsync(CancellationToken cancellationToken)
    {
        try
        {
            _roles = await _gateway.GetRoles().WaitAsync(cancellationToken);
        }
        catch (RequestFailedException exception)
        {
            LogRolesNotLoaded(LogName, exception.Code);
            _roles = [];
        }
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
