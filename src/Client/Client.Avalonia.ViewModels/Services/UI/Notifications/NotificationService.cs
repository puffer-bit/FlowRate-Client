using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using ReactiveUI;
using Client.ViewModels.Notifications;

namespace Client.Services;

public class NotificationService : ReactiveObject, INotificationService
{
    // More than this would climb over the content of the window: the oldest one leaves for a new one
    private const int MaxVisible = 4;
    // The history is kept in memory only; the oldest entries give way
    private const int MaxHistory = 100;

    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProblemLifetime = TimeSpan.FromSeconds(8);

    private readonly ObservableCollection<ToastVM> _notifications = [];

    private readonly ObservableCollection<ToastVM> _history = [];

    public ReadOnlyObservableCollection<ToastVM> Notifications { get; }

    public ReadOnlyObservableCollection<ToastVM> History { get; }

    public bool HasHistory => _history.Count > 0;

    public bool HasUnread
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsSoundMuted
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public NotificationService()
    {
        Notifications = new ReadOnlyObservableCollection<ToastVM>(_notifications);
        History = new ReadOnlyObservableCollection<ToastVM>(_history);
        _history.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasHistory));
    }

    public void MarkAllRead() => HasUnread = false;

    public void RemoveFromHistory(ToastVM notification) => _history.Remove(notification);

    public void ClearHistory() => _history.Clear();

    public void Show(NotificationType type, string title, string? message = null, TimeSpan? lifetime = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(type, title, message, lifetime));
            return;
        }

        var toast = new ToastVM(type, title, message, toast => _notifications.Remove(toast));
        _notifications.Add(toast);

        _history.Insert(0, toast);
        while (_history.Count > MaxHistory)
            _history.RemoveAt(_history.Count - 1);
        HasUnread = true;

        // Those that are already leaving free their place themselves; the one under the pointer is being read
        var staying = 0;
        for (var index = _notifications.Count - 1; index >= 0; index--)
        {
            if (_notifications[index].IsLeaving || _notifications[index].IsPointerOver)
                continue;

            if (++staying > MaxVisible)
                _ = _notifications[index].CloseAsync();
        }

        var stays = lifetime ?? (type is NotificationType.Error or NotificationType.Warning
            ? ProblemLifetime
            : DefaultLifetime);

        if (stays > TimeSpan.Zero)
            toast.StartLifetime(stays);
    }
}
