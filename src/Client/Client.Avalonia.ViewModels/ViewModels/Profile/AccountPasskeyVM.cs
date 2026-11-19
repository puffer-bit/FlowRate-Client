using System;
using Client.Avalonia.Localization;
using Client.Core.Users;

namespace Client.ViewModels.Profile;

public sealed class AccountPasskeyVM(AccountPasskey passkey)
{
    public int Id => passkey.Id;

    public string Label => string.IsNullOrWhiteSpace(passkey.Label)
        ? Strings.Get("accountSecurity.passkeys.unnamed")
        : passkey.Label;

    public string Details => passkey.LastUsedAtUtc is { } lastUsed
        ? Strings.Format("accountSecurity.passkeys.usedAt", ("created", Format(passkey.CreatedAtUtc)),
            ("used", Format(lastUsed)))
        : Strings.Format("accountSecurity.passkeys.createdAt", ("created", Format(passkey.CreatedAtUtc)));

    private static string Format(DateTime time) =>
        Strings.FormatDateTime(time.Kind == DateTimeKind.Local ? time : DateTime.SpecifyKind(time, DateTimeKind.Utc).ToLocalTime());
}
