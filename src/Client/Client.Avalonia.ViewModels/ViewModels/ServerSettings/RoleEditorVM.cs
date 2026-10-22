using System;
using System.Collections.Generic;
using System.Linq;
using ReactiveUI;
using Shared.Enums;
using Shared.Validation;
using Client.Avalonia.Localization;
using Client.Core.Coordinator;

namespace Client.ViewModels.ServerSettings;

/// <summary>
/// The role chosen in the roles tab, being edited: its name, color and permissions. What may be changed follows
/// the roles of the current user, as the server checks it.
/// </summary>
public sealed class RoleEditorVM : ReactiveObject
{
    private const string EveryoneName = "@everyone";

    // Set while the values are filled in, so that filling them is not an edit
    private bool _isLoading;

    /// <summary>The role as it was loaded; null when none is chosen.</summary>
    public RoleModel? Role
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasRole));
            this.RaisePropertyChanged(nameof(IsEveryone));
        }
    }

    public bool HasRole => Role != null;

    public bool IsEveryone => Role?.IsEveryone ?? false;

    public string Name
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            NameError = IsEveryone ? null : Validate(value);
            OnEdited();
        }
    } = string.Empty;

    public string? NameError
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public IReadOnlyList<RoleColorVM> Colors { get; }

    public IReadOnlyList<PermissionOptionVM> Permissions { get; }

    /// <summary>The role is below the highest role of the current user, who may manage roles.</summary>
    public bool CanEdit
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool CanDelete => CanEdit && !IsEveryone;

    public bool IsDirty
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The role was saved elsewhere while it had unsaved edits here; it cannot be saved over them.</summary>
    public bool IsChangedElsewhere
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public RoleEditorVM()
    {
        Colors = new[] { (int?)null }.Concat(RolesTabVM.Palette.Select(color => (int?)color))
            .Select(color => new RoleColorVM(color, Select))
            .ToList();
        Permissions = PermissionOptions.Select(option =>
                new PermissionOptionVM(option.Permission, Strings.Get(option.Title), Strings.Get(option.Description),
                    OnEdited))
            .ToList();
    }

    public int? SelectedColor => Colors.FirstOrDefault(color => color.IsSelected)?.Color;

    /// <summary>Fills the editor with the role, dropping any edits.</summary>
    public void Load(RoleModel? role, CoordinatorPermissions permissions)
    {
        _isLoading = true;
        try
        {
            Role = role;
            Name = role?.Name ?? string.Empty;
            foreach (var color in Colors)
                color.IsSelected = color.Color == role?.Color;
            foreach (var option in Permissions)
                option.IsChecked = role != null && role.Permissions.HasFlag(option.Permission);
            IsChangedElsewhere = false;
        }
        finally
        {
            _isLoading = false;
        }

        ApplyAccess(permissions);
        OnEdited();
    }

    /// <summary>
    /// Takes a newer state of the role being edited: at once when there are no edits, otherwise it only tells
    /// that the role changed elsewhere.
    /// </summary>
    public void Follow(RoleModel? role, CoordinatorPermissions permissions)
    {
        if (role == null || Role == null || role.Id != Role.Id || (role.Revision != Role.Revision && !IsDirty))
        {
            Load(role, permissions);
            return;
        }

        if (role.Revision != Role.Revision)
            IsChangedElsewhere = true;

        ApplyAccess(permissions);
    }

    public RoleChange ToChange() => new(Name.Trim(), SelectedColor,
        Permissions.Where(option => option.IsChecked)
            .Aggregate(Permission.None, (all, option) => all | option.Permission));

    private void ApplyAccess(CoordinatorPermissions permissions)
    {
        CanEdit = Role != null && permissions.CanManage(Role);
        this.RaisePropertyChanged(nameof(CanDelete));

        // A permission the role has may stay; only the ones of the current user may be added
        foreach (var option in Permissions)
            option.IsEnabled = CanEdit && (permissions.CanGrant(option.Permission)
                                           || Role!.Permissions.HasFlag(option.Permission));
    }

    private void Select(RoleColorVM selected)
    {
        if (!CanEdit || IsEveryone)
            return;

        foreach (var color in Colors)
            color.IsSelected = color == selected;
        OnEdited();
    }

    private void OnEdited()
    {
        if (_isLoading || Permissions == null)
            return;

        IsDirty = Role != null && ToChange() is var change
                               && ((!IsEveryone && (change.Name != Role.Name || change.Color != Role.Color))
                                   || change.Permissions != Role.Permissions);
    }

    private static string? Validate(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
            return Strings.Get("roles.name.empty");
        if (trimmed.Length > Limits.RoleNameMaxLength)
            return Strings.Format("roles.name.tooLong", ("limit", Limits.RoleNameMaxLength));
        if (trimmed.Any(char.IsControl))
            return Strings.Get("profile.name.controlCharacters");
        if (trimmed == EveryoneName)
            return Strings.Get("roles.name.reserved");

        return null;
    }

    /// <summary>Every permission the editor offers, in its order, with the keys of its texts.</summary>
    private static readonly (Permission Permission, string Title, string Description)[] PermissionOptions =
    [
        (Permission.Administrator, "roles.permission.administrator", "roles.permission.administrator.description"),
        (Permission.ViewCoordinatorSettings, "roles.permission.viewSettings", "roles.permission.viewSettings.description"),
        (Permission.ManageCoordinatorSettings, "roles.permission.manageSettings", "roles.permission.manageSettings.description"),
        (Permission.ManageRoles, "roles.permission.manageRoles", "roles.permission.manageRoles.description"),
        (Permission.CreateSessions, "roles.permission.createSessions", "roles.permission.createSessions.description"),
        (Permission.ManageSessions, "roles.permission.manageSessions", "roles.permission.manageSessions.description"),
        (Permission.KickMembers, "roles.permission.kick", "roles.permission.kick.description"),
        (Permission.BanMembers, "roles.permission.ban", "roles.permission.ban.description"),
        (Permission.ApproveMembers, "roles.permission.approve", "roles.permission.approve.description"),
        (Permission.ManageMessages, "roles.permission.manageMessages", "roles.permission.manageMessages.description"),
        (Permission.ManageUsers, "roles.permission.manageUsers", "roles.permission.manageUsers.description"),
        (Permission.ViewSessions, "roles.permission.viewSessions", "roles.permission.viewSessions.description"),
        (Permission.SendMessages, "roles.permission.sendMessages", "roles.permission.sendMessages.description"),
        (Permission.AttachFiles, "roles.permission.attachFiles", "roles.permission.attachFiles.description"),
        (Permission.JoinSessions, "roles.permission.joinSessions", "roles.permission.joinSessions.description"),
        (Permission.Speak, "roles.permission.speak", "roles.permission.speak.description"),
        (Permission.ShareVideo, "roles.permission.shareVideo", "roles.permission.shareVideo.description")
    ];
}

/// <summary>One permission in the role editor.</summary>
public sealed class PermissionOptionVM(Permission permission, string title, string description, Action onChanged)
    : ReactiveObject
{
    public Permission Permission { get; } = permission;
    public string Title { get; } = title;
    public string Description { get; } = description;

    public bool IsChecked
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            onChanged();
        }
    }

    public bool IsEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }
}

/// <summary>One color offered in the role editor; null for none.</summary>
public sealed class RoleColorVM(int? color, Action<RoleColorVM> select) : ReactiveObject
{
    /// <summary>0xRRGGBB, or null for no color.</summary>
    public int? Color { get; } = color;

    public bool IsNone => Color == null;

    public bool IsSelected
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public void Select() => select(this);
}
