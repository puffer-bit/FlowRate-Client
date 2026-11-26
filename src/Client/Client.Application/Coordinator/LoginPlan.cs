using Client.Core.Settings;

namespace Client.Application.Coordinator;

/// <summary>
/// How a connect logs in. A device the account knows signs a challenge; every other way adds a new device to the
/// account, which the reconnects then log in as. <see cref="IsTemporary"/> devices keep their key in memory only and
/// end after a day without a login.
/// </summary>
public abstract record LoginPlan(bool IsTemporary = false)
{
    /// <summary>How a saved account logs in without asking; null when it cannot and the user has to log in.</summary>
    public static LoginPlan? For(SavedUserModel user) =>
        user is { DeviceId: { } deviceId, DeviceKey: { } key } ? new DeviceLoginPlan(deviceId, key)
        : !string.IsNullOrEmpty(user.UserIdentity)
            ? new LegacyLoginPlan(user.UserIdentity, user.SessionToken, user.AccountPassword)
            : null;
}

/// <summary>A device of the account, with the name of its key in the key store.</summary>
public sealed record DeviceLoginPlan(Guid DeviceId, string KeyName, bool Temporary = false) : LoginPlan(Temporary);

/// <summary>A new account, made by this device; the server picks its name.</summary>
public sealed record RegisterLoginPlan : LoginPlan;

/// <summary>The recovery phrase of the account; it alone adds the device.</summary>
public sealed record RecoveryLoginPlan(string Phrase, bool Temporary = false) : LoginPlan(Temporary);

/// <summary>The username; the password and one-time code are asked through the login prompt.</summary>
public sealed record UsernameLoginPlan(string Username, bool Temporary = false) : LoginPlan(Temporary);

/// <summary>A one-time login code from the control panel.</summary>
public sealed record LoginCodeLoginPlan(string Code, bool Temporary = false) : LoginPlan(Temporary);

/// <summary>Waits for a device of the account to approve this one, by the code it shows.</summary>
public sealed record DeviceLinkLoginPlan(bool Temporary = false) : LoginPlan(Temporary);

/// <summary>
/// The identity of the earlier protocol 3, with its session token and the account password it remembered: logs in
/// once and moves the account over to a device and a recovery phrase.
/// </summary>
public sealed record LegacyLoginPlan(string Identity, string? SessionToken, string? AccountPassword) : LoginPlan;

/// <summary>The code a new device shows to be added by a device of the account.</summary>
public sealed record DeviceLinkCode(string Code, DateTimeOffset ExpiresAt);
