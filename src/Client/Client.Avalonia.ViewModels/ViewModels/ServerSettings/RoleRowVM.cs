using Client.Core.Coordinator;

namespace Client.ViewModels.ServerSettings;

/// <summary>A role in the list of the roles tab.</summary>
public sealed class RoleRowVM(RoleModel role, bool canManage, bool canMoveUp, bool canMoveDown)
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
}
