using System;
using System.Linq;
using Client.Avalonia.Localization;
using Client.Core.Users;

namespace Client.ViewModels.Profile;

/// <summary>A device of the account, as the list of devices shows it.</summary>
public sealed class AccountSessionVM(AccountDevice device)
{
    public Guid Id => device.Id;
    public bool IsCurrent => device.IsCurrent;
    public bool IsOther => !device.IsCurrent;

    public string Name => string.IsNullOrWhiteSpace(device.DeviceName)
        ? Strings.Get("accountSecurity.sessions.unnamed")
        : device.DeviceName;

    /// <summary>"Linux · 0.8.0 · this device", "Android · 0.8.0 · online", or when it was last used, and from where.</summary>
    public string Details => string.Join(" · ", new[]
    {
        device.Platform,
        device.AppVersion,
        device.IsCurrent ? Strings.Get("accountSecurity.sessions.current")
        : device.IsOnline ? Strings.Get("accountSecurity.sessions.online")
        : Strings.Format("accountSecurity.sessions.lastUsed", ("time", Format(device.LastUsedAtUtc))),
        device.IsTemporary ? Strings.Get("accountSecurity.sessions.temporary") : null,
        device.LastAddress
    }.Where(part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>How the device was added: "recovery phrase", "approved by another device"...</summary>
    public string AddedBy => Strings.Format("accountSecurity.sessions.addedBy",
        ("method", Strings.Get("devices.addedBy." + device.AddedBy)), ("time", Format(device.CreatedAtUtc)));

    private static string Format(DateTimeOffset time) =>
        Strings.FormatDateTime(time.ToLocalTime());
}
