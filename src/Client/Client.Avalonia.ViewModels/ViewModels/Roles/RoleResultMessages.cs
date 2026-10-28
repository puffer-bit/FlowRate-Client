using Shared.Enums;
using Client.Core.Coordinator;
using Client.Avalonia.Localization;

namespace Client.ViewModels.Roles;

/// <summary>What the role dialogs tell about a refused role change.</summary>
public static class RoleResultMessages
{
    /// <returns>Null when there is nothing to tell: done, or refused before it was sent, which the main window tells.</returns>
    public static string? Describe(RequestResult result) => result switch
    {
        { IsSuccess: true } or { IsRejected: true } => null,
        { Error: ErrorCode.HierarchyViolation } => Strings.Get("roles.error.hierarchy"),
        { Error: ErrorCode.Conflict } => Strings.Get("roles.error.conflict"),
        { Error: ErrorCode.RoleNotFound } => Strings.Get("roles.error.roleNotFound"),
        { Error: ErrorCode.UserNotFound } => Strings.Get("roles.error.userNotFound"),
        { Error: ErrorCode.InvalidValue } => Strings.Get("roles.error.invalid"),
        { Error: ErrorCode.TooManyRoles } => Strings.Get("roles.error.tooMany"),
        { Error: ErrorCode.TimedOut } => Strings.Get("roles.error.timedOut"),
        _ => Strings.Format("roles.error.failed", ("reason", result))
    };
}
