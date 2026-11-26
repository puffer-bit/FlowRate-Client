using Client.Core.Settings;
using Shared.Enums.User;

namespace Client.Application.Coordinator.Users;

/// <summary>The idle level a device reports for the time its user has gone without input.</summary>
public static class IdleLevels
{
    /// <param name="idleTime">The time since the last input; null where it cannot be known, which stays active.</param>
    /// <param name="isInCall">Whether the user is in a call now.</param>
    public static IdleLevel Of(TimeSpan? idleTime, BehaviorSettings settings, bool isInCall)
    {
        if (idleTime is not { } idle || !settings.IsAutoIdleEnabled || isInCall && settings.KeepActiveDuringCalls)
            return IdleLevel.Active;

        if (settings.SleepAfterMinutes > 0 && idle >= TimeSpan.FromMinutes(settings.SleepAfterMinutes))
            return IdleLevel.Sleeping;

        if (settings.IdleAfterMinutes > 0 && idle >= TimeSpan.FromMinutes(settings.IdleAfterMinutes))
            return IdleLevel.Idle;

        return IdleLevel.Active;
    }
}
