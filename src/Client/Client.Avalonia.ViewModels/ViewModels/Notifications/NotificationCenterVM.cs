using System.Collections.ObjectModel;
using Client.Services;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Notifications;

/// <summary>
/// What the views of <see cref="INotificationService"/> are bound to: the notifications in the
/// corner of the window and the notification center with everything shown before.
/// </summary>
public class NotificationCenterVM : ReactiveObject
{
    private readonly INotificationService _notifications;

    public ReadOnlyObservableCollection<ToastVM> Notifications => _notifications.Notifications;

    public ReadOnlyObservableCollection<ToastVM> History => _notifications.History;

    public bool HasHistory => _notifications.HasHistory;

    public bool HasUnread => _notifications.HasUnread;

    public bool IsSoundMuted => _notifications.IsSoundMuted;

    public ReactiveCommand<RxVoid, RxVoid> ToggleSoundCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ClearHistoryCommand { get; }
    public ReactiveCommand<ToastVM, RxVoid> RemoveCommand { get; }

    public NotificationCenterVM(INotificationService notifications)
    {
        _notifications = notifications;

        // The properties have the names of those of the service, which lives as long as the application
        _notifications.PropertyChanged += (_, e) => this.RaisePropertyChanged(e.PropertyName);

        ToggleSoundCommand = ReactiveCommand.Create(() => { _notifications.IsSoundMuted = !_notifications.IsSoundMuted; });
        ClearHistoryCommand = ReactiveCommand.Create(_notifications.ClearHistory);
        RemoveCommand = ReactiveCommand.Create<ToastVM>(_notifications.RemoveFromHistory);
    }

    /// <summary>
    /// The notification center has been looked at. Called on the UI thread.
    /// </summary>
    public void MarkAllRead() => _notifications.MarkAllRead();
}
