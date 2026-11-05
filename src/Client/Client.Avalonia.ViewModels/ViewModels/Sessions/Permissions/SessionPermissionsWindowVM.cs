using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Avalonia.Localization;
using Client.Core.Coordinator;
using Client.Core.Sessions;
using ReactiveUI;
using Shared.Enums;
using Shared.Permissions;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Permissions;

/// <summary>
/// The dialog that changes what roles may do in one session. The saved overrides come back with the session
/// the server sends to every client.
/// </summary>
public sealed class SessionPermissionsWindowVM : ReactiveObject, IDisposable
{
    private readonly SessionModel _session;
    private readonly ReactiveCommand<(int SessionId, IReadOnlyList<PermissionOverride> Overrides),
        RequestResult> _changePermissionsCommand;
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
            RequestResult> changePermissionsCommand)
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
            case { IsSuccess: true }:
                await CloseWindow.Handle(RxVoid.Default);
                break;
            case { Error: ErrorCode.SessionNotFound }:
                GeneralError = Strings.Get("sessionSettings.sessionGone");
                break;
            case { Error: ErrorCode.RoleNotFound }:
                GeneralError = Strings.Get("sessionPermissions.roleGone");
                break;
            case { Error: ErrorCode.InsufficientPermissions }:
                GeneralError = Strings.Get("sessionPermissions.notAllowed");
                break;
            case { Error: ErrorCode.TimedOut }:
                GeneralError = Strings.Get("profile.name.timedOut");
                break;
            // The main window tells why
            case { IsRejected: true }:
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
        new(Permission.ShareVideo, "roles.permission.shareVideo", IsHybridOnly: true),
        new(Permission.PokeMembers, "roles.permission.poke", IsHybridOnly: true)
    ];
}

internal sealed record SessionPermissionOption(Permission Permission, string Title, bool IsChatOnly = false,
    bool IsHybridOnly = false);
