namespace Client.Core.Users;

/// <summary>What the current user logs in with besides the identity. Secrets are never sent.</summary>
/// <param name="IsTwoFactorPending">A secret was offered and waits for its first code.</param>
/// <param name="PasskeysAvailable">False when the server has no passkeys set up; none can be added.</param>
public sealed record AccountSecurity(
    bool HasPassword,
    bool IsTwoFactorEnabled,
    bool IsTwoFactorPending,
    bool PasskeysAvailable,
    IReadOnlyList<AccountPasskey> Passkeys);

public sealed record AccountPasskey(int Id, string Label, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastUsedAtUtc);

/// <summary>
/// The current password and one-time code, asked for by every change while the user has them. Each part is null
/// when the user does not have it.
/// </summary>
public sealed record AccountProof(string? Password, string? TwoFactorCode);

/// <summary>A secret for an authenticator app, offered until a code made from it turns the codes on.</summary>
/// <param name="Secret">Base32, for typing into the app.</param>
/// <param name="Uri">otpauth:// URI, for a QR code.</param>
public sealed record TwoFactorSetup(string Secret, string Uri);

/// <summary>What the server asks for to finish a login by username.</summary>
public sealed record LoginChallenge(bool AsksPassword, bool AsksTwoFactorCode, bool AcceptsPasskey)
{
    /// <summary>A password or a code would do; a user with passkeys alone cannot log in from here.</summary>
    public bool AcceptsCodes => AsksPassword || AsksTwoFactorCode;
}

/// <summary>A device of the current user's account.</summary>
/// <param name="LastUsedAtUtc">The last login of the device; it ends after a while without one.</param>
/// <param name="IsCurrent">This device.</param>
/// <param name="IsOnline">The device is connected now.</param>
/// <param name="IsTemporary">Not remembered: it ends after a day without a login.</param>
/// <param name="AddedBy">How the device was added to the account.</param>
public sealed record AccountDevice(
    Guid Id,
    string DeviceName,
    string Platform,
    string AppVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastUsedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string? LastAddress,
    bool IsCurrent,
    bool IsOnline,
    bool IsTemporary = false,
    Shared.Enums.Auth.DeviceLoginMethod AddedBy = Shared.Enums.Auth.DeviceLoginMethod.Registration);

/// <summary>What a new device that waits to be added tells about itself.</summary>
/// <param name="Address">The IP address it connected from.</param>
public sealed record DeviceLinkInfo(string? DeviceName, string? Platform, string? AppVersion, string? Address);
