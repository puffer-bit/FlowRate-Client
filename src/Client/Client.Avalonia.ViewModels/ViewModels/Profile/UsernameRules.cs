using Client.Avalonia.Localization;
using System.Linq;
using Shared.Enums.User;
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
    public static string? DescribeResult(ChangeUsernameResult result) => result switch
    {
        ChangeUsernameResult.NoError => null,
        ChangeUsernameResult.UsernameAlreadyUsed => Strings.Get("profile.name.taken"),
        ChangeUsernameResult.InvalidUsername => Strings.Get("profile.name.notAccepted"),
        ChangeUsernameResult.UserNotFound => Strings.Get("profile.name.userNotConnected"),
        ChangeUsernameResult.TimedOut => Strings.Get("profile.name.timedOut"),
        // The main window tells why; the field stays filled in for another try
        ChangeUsernameResult.Rejected => null,
        _ => Strings.Format("profile.name.notChanged", ("reason", result))
    };
}
