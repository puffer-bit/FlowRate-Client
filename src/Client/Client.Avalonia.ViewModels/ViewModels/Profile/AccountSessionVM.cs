using System;
using System.Linq;
using System.Reactive.Linq;
using Client.Avalonia.Localization;
using Client.Core.Users;
using ReactiveUI;

namespace Client.ViewModels.Profile;

public sealed class AccountSessionVM(AccountSession session)
{
    public Guid Id => session.Id;
    public bool IsCurrent => session.IsCurrent;
    public bool IsOther => !session.IsCurrent;

    public string Name => string.IsNullOrWhiteSpace(session.DeviceName)
        ? Strings.Get("accountSecurity.sessions.unnamed")
        : session.DeviceName;

    /// <summary>"Linux · 0.8.0 · this device", "Android · 0.8.0 · online", or when it was last used, and from where.</summary>
    public string Details => string.Join(" · ", new[]
    {
        session.Platform,
        session.AppVersion,
        session.IsCurrent ? Strings.Get("accountSecurity.sessions.current")
        : session.IsOnline ? Strings.Get("accountSecurity.sessions.online")
        : Strings.Format("accountSecurity.sessions.lastUsed", ("time", Format(session.LastUsedAtUtc))),
        session.LastAddress
    }.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static string Format(DateTime time) =>
        Strings.FormatDateTime(DateTime.SpecifyKind(time, DateTimeKind.Utc).ToLocalTime());
}
