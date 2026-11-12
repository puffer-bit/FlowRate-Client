using System.Collections.Generic;
using Client.Core.Coordinator;
using Client.Core.Users;
using ReactiveUI;
using Shared.Enums.User;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.ServerSettings;

/// <summary>A user in the list of the users tab, with the commands of the tab for its menu.</summary>
public sealed class ServerUserRowVM(UserModel user, IReadOnlyList<RoleModel> roles, bool isSelf,
    bool canEditProfile, bool canModerate, ServerUsersTabVM tab)
{
    // The menu opens in a popup, out of reach of the tab; each command takes the row
    public ReactiveCommand<ServerUserRowVM, RxVoid> EditProfileCommand => tab.EditProfileCommand;
    public ReactiveCommand<ServerUserRowVM, RxVoid> ToggleFreezeCommand => tab.ToggleFreezeCommand;
    public ReactiveCommand<ServerUserRowVM, RxVoid> ToggleBanCommand => tab.ToggleBanCommand;
    public ReactiveCommand<ServerUserRowVM, RxVoid> DeleteCommand => tab.DeleteCommand;

    public UserModel User { get; } = user;
    public string Username => User.Username;
    public string? AvatarUrl => User.AvatarUrl;
    public UserStatus Status => User.Status;
    public bool IsBanned => User.State == UserState.Banned;
    public bool IsFrozen => User.State == UserState.Suspended;

    /// <summary>Highest first, without the everyone role.</summary>
    public IReadOnlyList<RoleModel> Roles { get; } = roles;

    /// <summary>The current user; nothing can be done to them here.</summary>
    public bool IsSelf { get; } = isSelf;

    public bool CanEditProfile { get; } = canEditProfile;

    /// <summary>The user can be frozen, banned and deleted, and brought back.</summary>
    public bool CanModerate { get; } = canModerate;

    public bool CanFreeze => CanModerate && !IsFrozen;
    public bool CanUnfreeze => CanModerate && IsFrozen;
    public bool CanBan => CanModerate && !IsBanned;
    public bool CanUnban => CanModerate && IsBanned;

    /// <summary>The menu has something in it.</summary>
    public bool HasActions => CanEditProfile || CanModerate;
}
