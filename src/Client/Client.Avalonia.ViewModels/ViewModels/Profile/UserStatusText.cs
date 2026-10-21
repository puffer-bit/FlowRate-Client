using Client.Avalonia.Localization;
using Shared.Enums.User;

namespace Client.ViewModels.Profile;

/// <summary>The name of a user status as the interface shows it.</summary>
public static class UserStatusText
{
    public static string Of(UserStatus status) => Strings.Get(status switch
    {
        UserStatus.Online => "profile.status.online",
        UserStatus.Away => "profile.status.away",
        UserStatus.DoNotDisturb => "profile.status.doNotDisturb",
        UserStatus.Invisible => "profile.status.invisible",
        _ => "profile.status.offline"
    });
}
