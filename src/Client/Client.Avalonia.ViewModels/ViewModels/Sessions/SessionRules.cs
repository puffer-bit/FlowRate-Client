using Client.Avalonia.Localization;
using System.Linq;
using Shared.Validation;

namespace Client.ViewModels.Sessions;

/// <summary>
/// What the session dialogs tell about a name and a number of participants before they are sent.
/// Mirrors CreateSessionModel on the server and ICoordinatorSession.ValidateSession, with a reason.
/// </summary>
public static class SessionRules
{
    public const decimal CapacityMin = Limits.SessionCapacityMin;
    public const decimal CapacityMax = Limits.SessionCapacityMax;

    public static string CapacityHint => Strings.Format("createSession.capacity.hint",
        ("min", Limits.SessionCapacityMin), ("max", Limits.SessionCapacityMax));

    public static string? ValidateName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
            return Strings.Get("profile.name.empty");
        if (trimmed.Length > Limits.SessionNameMaxLength)
            return Strings.Format("profile.name.tooLong", ("limit", Limits.SessionNameMaxLength));
        if (trimmed.Any(char.IsControl))
            return Strings.Get("profile.name.controlCharacters");

        return null;
    }

    public static string? ValidateCapacity(decimal? capacity) => capacity switch
    {
        null => Strings.Get("createSession.capacity.empty"),
        < Limits.SessionCapacityMin => Strings.Format("createSession.capacity.atLeast", ("min", Limits.SessionCapacityMin)),
        > Limits.SessionCapacityMax => Strings.Format("createSession.capacity.atMost", ("max", Limits.SessionCapacityMax)),
        _ when capacity != decimal.Truncate(capacity.Value) => Strings.Get("createSession.capacity.wholeNumber"),
        _ => null
    };
}
