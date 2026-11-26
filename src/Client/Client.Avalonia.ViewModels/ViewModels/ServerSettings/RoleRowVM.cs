using System.Windows.Input;
using Client.Core.Coordinator;

namespace Client.ViewModels.ServerSettings;

/// <summary>A role in the list of the roles tab.</summary>
public sealed class RoleRowVM(RolesTabVM tab, RoleModel role, bool canManage, bool canMoveUp, bool canMoveDown)
{
    public RoleModel Role { get; } = role;
    public string Name => Role.Name;

    /// <summary>0xRRGGBB, or null for no color.</summary>
    public int? Color => Role.Color;

    public bool IsEveryone => Role.IsEveryone;

    /// <summary>The role is below the highest role of the current user, who may manage roles.</summary>
    public bool CanManage { get; } = canManage;

    public bool CanMoveUp { get; } = canMoveUp;
    public bool CanMoveDown { get; } = canMoveDown;
    public bool CanDelete => CanManage && !IsEveryone;

    // The context menu of the row; through the commands of the tab, which tell it is busy
    public void MoveUp() => Execute(tab.MoveUpCommand);
    public void MoveDown() => Execute(tab.MoveDownCommand);
    public void Delete() => Execute(tab.DeleteCommand);

    private void Execute(ICommand command)
    {
        if (command.CanExecute(this))
            command.Execute(this);
    }
}
