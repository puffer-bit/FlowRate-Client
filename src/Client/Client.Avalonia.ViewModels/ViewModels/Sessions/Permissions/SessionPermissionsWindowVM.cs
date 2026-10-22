using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using Shared.Enums;
using Shared.Permissions;
using Client.Avalonia.Localization;
using Client.Core.Coordinator;
using Client.Core.Sessions;

namespace Client.ViewModels.Sessions.Permissions;

/// <summary>
/// The dialog that changes what roles may do in one session. The saved overrides come back with the session
/// the server sends to every client.
/// </summary>
public sealed class SessionPermissionsWindowVM : ReactiveObject, IDisposable
{
    private readonly SessionModel _session;
    private readonly ReactiveCommand<(int SessionId, IReadOnlyList<PermissionOverride> Overrides),
        ChangeSessionPermissionsResult> _changePermissionsCommand;
    private readonly ObservableAsPropertyHelper<bool> _isBusy;

    public string SessionName => _session.Name;

    /// <summary>All roles, highest first; those the current user may not change are shown read-only.</summary>
    public IReadOnlyList<SessionRoleOverrideVM> Roles { get; }

    public SessionRoleOverrideVM? SelectedRole
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string? GeneralError
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsBusy => _isBusy.Value;

    public Interaction<RxVoid, RxVoid> CloseWindow { get; } = new();

    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }

    public SessionPermissionsWindowVM(SessionModel session, CoordinatorPermissions permissions,
        ReactiveCommand<(int SessionId, IReadOnlyList<PermissionOverride> Overrides),
            ChangeSessionPermissionsResult> changePermissionsCommand)
    {
        _session = session;
        _changePermissionsCommand = changePermissionsCommand;

        var options = PermissionOptions
            .Where(option => session.SessionType == SessionType.Hybrid ? !option.IsChatOnly : !option.IsHybridOnly)
            .ToList();
        Roles = permissions.Roles
            .OrderByDescending(role => role.Position)
            .Select(role =>
            {
                // A struct: without the fallback a role with no override would get role 0
                var current = session.PermissionOverrides.FirstOrDefault(o => o.RoleId == role.Id,
                    new PermissionOverride(role.Id, Permission.None, Permission.None));
                return new SessionRoleOverrideVM(role, current, options,
                    flag => permissions.CanOverride(role, flag, Permission.None));
            })
            .ToList();
        SelectedRole = Roles.FirstOrDefault(role => role.IsEditable) ?? Roles.FirstOrDefault();

        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        CloseCommand = ReactiveCommand.CreateFromTask(async () => { await CloseWindow.Handle(RxVoid.Default); },
            SaveCommand.IsExecuting.Select(busy => !busy));
        _isBusy = SaveCommand.IsExecuting.ToProperty(this, nameof(IsBusy));
    }

    private async Task SaveAsync()
    {
        GeneralError = null;

        var overrides = Roles.Select(role => role.ToOverride())
            .Where(o => o.Allow != Permission.None || o.Deny != Permission.None)
            .ToList();
        var result = await _changePermissionsCommand.Execute((_session.Id, overrides));

        switch (result)
        {
            case ChangeSessionPermissionsResult.NoError:
                await CloseWindow.Handle(RxVoid.Default);
                break;
            case ChangeSessionPermissionsResult.SessionNotExists:
                GeneralError = Strings.Get("sessionSettings.sessionGone");
                break;
            case ChangeSessionPermissionsResult.RoleNotExists:
                GeneralError = Strings.Get("sessionPermissions.roleGone");
                break;
            case ChangeSessionPermissionsResult.NotAllowed:
                GeneralError = Strings.Get("sessionPermissions.notAllowed");
                break;
            case ChangeSessionPermissionsResult.TimedOut:
                GeneralError = Strings.Get("profile.name.timedOut");
                break;
            // The main window tells why
            case ChangeSessionPermissionsResult.Rejected:
                break;
            default:
                GeneralError = Strings.Format("sessionSettings.notSaved", ("reason", result));
                break;
        }
    }

    public void Dispose()
    {
        _isBusy.Dispose();
        SaveCommand.Dispose();
        CloseCommand.Dispose();
    }

    /// <summary>Every permission a session can override, in its order, with the keys of its texts.</summary>
    private static readonly SessionPermissionOption[] PermissionOptions =
    [
        new(Permission.ViewSessions, "roles.permission.viewSessions"),
        new(Permission.SendMessages, "roles.permission.sendMessages", IsChatOnly: true),
        new(Permission.AttachFiles, "roles.permission.attachFiles", IsChatOnly: true),
        new(Permission.ManageMessages, "roles.permission.manageMessages", IsChatOnly: true),
        new(Permission.JoinSessions, "roles.permission.joinSessions", IsHybridOnly: true),
        new(Permission.Speak, "roles.permission.speak", IsHybridOnly: true),
        new(Permission.ShareVideo, "roles.permission.shareVideo", IsHybridOnly: true)
    ];
}

internal sealed record SessionPermissionOption(Permission Permission, string Title, bool IsChatOnly = false,
    bool IsHybridOnly = false);

/// <summary>The override of one role in the dialog.</summary>
public sealed class SessionRoleOverrideVM
{
    private readonly RoleModel _role;
    // Flags the dialog does not show are kept as they were
    private readonly PermissionOverride _hidden;

    public string Name => _role.Name;
    public int? Color => _role.Color;
    public bool IsEditable { get; }
    public IReadOnlyList<SessionPermissionStateVM> Permissions { get; }

    internal SessionRoleOverrideVM(RoleModel role, PermissionOverride current,
        IReadOnlyList<SessionPermissionOption> options, Func<Permission, bool> canChange)
    {
        _role = role;
        var shown = options.Aggregate(Permission.None, (all, option) => all | option.Permission);
        _hidden = current with { Allow = current.Allow & ~shown, Deny = current.Deny & ~shown };

        IsEditable = canChange(Permission.None);
        Permissions = options.Select(option => new SessionPermissionStateVM(option.Permission,
                Strings.Get(option.Title),
                current.Allow.HasFlag(option.Permission) ? PermissionState.Allow
                : current.Deny.HasFlag(option.Permission) ? PermissionState.Deny
                : PermissionState.Inherit,
                canChange(option.Permission)))
            .ToList();
    }

    public PermissionOverride ToOverride() => Permissions.Aggregate(_hidden, (o, p) => p.State switch
    {
        PermissionState.Allow => o with { Allow = o.Allow | p.Permission },
        PermissionState.Deny => o with { Deny = o.Deny | p.Permission },
        _ => o
    });
}

public enum PermissionState
{
    Inherit,
    Allow,
    Deny
}

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
