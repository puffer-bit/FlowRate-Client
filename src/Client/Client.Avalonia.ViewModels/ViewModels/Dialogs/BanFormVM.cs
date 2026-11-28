using System;
using System.Reactive.Linq;
using Client.Avalonia.Localization;
using Client.Services;
using ReactiveUI;
using Shared.Enums.User;

namespace Client.ViewModels.Dialogs;

/// <summary>Only implemented ban kinds are offered. An empty address explicitly uses the user's last address.</summary>
public sealed class BanFormVM : ReactiveObject
{
    public string[] Kinds { get; } = [Strings.Get("ban.kind.account"), Strings.Get("ban.kind.ip")];
    public string[] Durations { get; } = [Strings.Get("ban.duration.permanent"), Strings.Get("ban.duration.hour"),
        Strings.Get("ban.duration.day"), Strings.Get("ban.duration.week"), Strings.Get("ban.duration.month")];
    public int KindIndex { get; set { this.RaiseAndSetIfChanged(ref field, value); this.RaisePropertyChanged(nameof(IsIp)); } }
    public int DurationIndex { get; set; }
    public string? Address { get; set; }
    public string? Reason { get; set => this.RaiseAndSetIfChanged(ref field, value); }
    public bool IsIp => KindIndex == 1;
    public BanKind Kind => IsIp ? BanKind.Ip : BanKind.Account;
    public DateTimeOffset? ExpiresAt => DurationIndex switch
    {
        1 => DateTimeOffset.UtcNow.AddHours(1), 2 => DateTimeOffset.UtcNow.AddDays(1),
        3 => DateTimeOffset.UtcNow.AddDays(7), 4 => DateTimeOffset.UtcNow.AddDays(30), _ => null
    };
    public IObservable<bool> CanSubmit => this.WatchProperty(nameof(Reason), vm => vm.Reason)
        .Select(value => (value?.Trim().Length ?? 0) <= 1000);
}
