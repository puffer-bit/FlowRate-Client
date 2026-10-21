using ReactiveUI;
using Client.Core.Coordinator;

namespace Client.ViewModels.Profile;

/// <summary>A role in the profile of a user: whether they have it, and whether the current user may change that.</summary>
public sealed class UserRoleVM : ReactiveObject
{
    public RoleModel Role { get; }

    public string Name => Role.Name;

    /// <summary>0xRRGGBB, or null for no color.</summary>
    public int? Color => Role.Color;

    public bool IsAssigned
    {
        get;
        set
        {
            field = value;
            // Also when unchanged: a check box clicked for a change that failed shows the value again
            this.RaisePropertyChanged();
        }
    }

    public bool CanChange { get; }

    public UserRoleVM(RoleModel role, bool isAssigned, bool canChange)
    {
        Role = role;
        IsAssigned = isAssigned;
        CanChange = canChange;
    }
}
