using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using Client.Avalonia.Localization;
using Client.Core.Coordinator;
using ReactiveUI;
using Shared.Enums;
using Shared.Permissions;

namespace Client.ViewModels.Sessions.Permissions;

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
