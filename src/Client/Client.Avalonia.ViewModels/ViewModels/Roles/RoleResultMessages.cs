using Client.Avalonia.Localization;
using Shared.Enums.Coordinator;

namespace Client.ViewModels.Roles;

/// <summary>What the role dialogs tell about a refused role change.</summary>
public static class RoleResultMessages
{
    /// <returns>Null when there is nothing to tell: done, or refused before it was sent, which the main window tells.</returns>
    public static string? Describe(CoordinatorRoleResult result) => result switch
    {
        CoordinatorRoleResult.NoError or CoordinatorRoleResult.Rejected => null,
        CoordinatorRoleResult.InsufficientPermissions => Strings.Get("roles.error.insufficientPermissions"),
        CoordinatorRoleResult.HierarchyViolation => Strings.Get("roles.error.hierarchy"),
        CoordinatorRoleResult.Conflict => Strings.Get("roles.error.conflict"),
        CoordinatorRoleResult.RoleNotFound => Strings.Get("roles.error.roleNotFound"),
        CoordinatorRoleResult.UserNotFound => Strings.Get("roles.error.userNotFound"),
        CoordinatorRoleResult.InvalidRole => Strings.Get("roles.error.invalid"),
        CoordinatorRoleResult.TooManyRoles => Strings.Get("roles.error.tooMany"),
        CoordinatorRoleResult.TimedOut => Strings.Get("roles.error.timedOut"),
        _ => Strings.Format("roles.error.failed", ("reason", result))
    };
}
