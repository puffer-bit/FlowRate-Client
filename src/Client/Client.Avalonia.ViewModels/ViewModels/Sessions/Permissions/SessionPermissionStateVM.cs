using ReactiveUI;
using Shared.Enums;

namespace Client.ViewModels.Sessions.Permissions;

/// <summary>One permission of a role in the dialog: as on the coordinator, allowed or denied.</summary>
public sealed class SessionPermissionStateVM(Permission permission, string title, PermissionState state,
    bool isEditable) : ReactiveObject
{
    public Permission Permission { get; } = permission;
    public string Title { get; } = title;
    public bool IsEditable { get; } = isEditable;

    public PermissionState State
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsInherited));
            this.RaisePropertyChanged(nameof(IsAllowed));
            this.RaisePropertyChanged(nameof(IsDenied));
        }
    } = state;

    public bool IsInherited
    {
        get => State == PermissionState.Inherit;
        set { if (value) State = PermissionState.Inherit; }
    }

    public bool IsAllowed
    {
        get => State == PermissionState.Allow;
        set { if (value) State = PermissionState.Allow; }
    }

    public bool IsDenied
    {
        get => State == PermissionState.Deny;
        set { if (value) State = PermissionState.Deny; }
    }
}
