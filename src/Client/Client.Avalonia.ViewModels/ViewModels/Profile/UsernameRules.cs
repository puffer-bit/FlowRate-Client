using Shared.Enums;
using Client.Core.Coordinator;
using Client.Avalonia.Localization;
using System.Linq;
using Shared.Validation;

namespace Client.ViewModels.Profile;

/// <summary>
/// What the profile dialogs tell about a name: before it is sent and after the server answered.
/// </summary>
public static class UsernameRules
{
    public const int MaxLength = Limits.UsernameMaxLength;

    public static string Hint => Strings.Format("profile.name.hint", ("limit", MaxLength));

    public static string? Validate(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
            return Strings.Get("profile.name.empty");
        if (trimmed.Length > MaxLength)
            return Strings.Format("profile.name.tooLong", ("limit", MaxLength));
        if (trimmed.Any(char.IsControl))
            return Strings.Get("profile.name.controlCharacters");

        return null;
    }

    /// <summary>
    /// The error to show under the name field, or null when there is none to show there.
    /// </summary>
    public static string? DescribeResult(RequestResult result) => result switch
    {
        { IsSuccess: true } => null,
        { Error: ErrorCode.NameTaken } => Strings.Get("profile.name.taken"),
        { Error: ErrorCode.InvalidValue } => Strings.Get("profile.name.notAccepted"),
        { Error: ErrorCode.UserNotFound } => Strings.Get("profile.name.userNotConnected"),
        { Error: ErrorCode.TimedOut } => Strings.Get("profile.name.timedOut"),
        // The main window tells why; the field stays filled in for another try
        { IsRejected: true } => null,
        _ => Strings.Format("profile.name.notChanged", ("reason", result))
    };
}
