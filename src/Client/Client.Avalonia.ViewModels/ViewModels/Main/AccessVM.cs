using System;
using ReactiveUI;
using Shared.Enums;
using Client.Core.Coordinator;

namespace Client.ViewModels.Main;

/// <summary>
/// What the current user may do on the server, for the views to offer only that. Follows the roles live;
/// the server checks every request anyway.
/// </summary>
public sealed class AccessVM : ReactiveObject
{
    public CoordinatorPermissions Permissions
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = CoordinatorPermissions.None;

    /// <summary>Raised on the UI thread after <see cref="Permissions"/> changed.</summary>
    public event Action<CoordinatorPermissions>? PermissionsUpdated;

    public bool CanViewServerSettings => Permissions.Has(Permission.ViewCoordinatorSettings);
    public bool CanEditServerSettings => Permissions.Has(Permission.ManageCoordinatorSettings);
    public bool CanManageRoles => Permissions.Has(Permission.ManageRoles);

    /// <summary>The server settings window has something to show: the settings or the roles.</summary>
    public bool CanOpenServerSettings => CanViewServerSettings || CanManageRoles;

    public bool CanCreateSessions => Permissions.Has(Permission.CreateSessions);

    /// <summary>Arranging sessions and categories, and changing any session.</summary>
    public bool CanManageSessions => Permissions.Has(Permission.ManageSessions);

    public bool CanManageMessages => Permissions.Has(Permission.ManageMessages);

    /// <summary>Must be called on the UI thread.</summary>
    public void Update(CoordinatorPermissions permissions)
    {
        Permissions = permissions;
        this.RaisePropertyChanged(nameof(CanViewServerSettings));
        this.RaisePropertyChanged(nameof(CanEditServerSettings));
        this.RaisePropertyChanged(nameof(CanManageRoles));
        this.RaisePropertyChanged(nameof(CanOpenServerSettings));
        this.RaisePropertyChanged(nameof(CanCreateSessions));
        this.RaisePropertyChanged(nameof(CanManageSessions));
        this.RaisePropertyChanged(nameof(CanManageMessages));
        PermissionsUpdated?.Invoke(permissions);
    }
}
