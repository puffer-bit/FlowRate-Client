using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Client.ViewModels.Notifications;

namespace Client.Services;

/// <summary>
/// Notifications of the whole application: any part of it shows one, and the main window lists them
/// in its bottom right corner whatever is open in it.
/// </summary>
public interface INotificationService : INotifyPropertyChanged
{
    /// <summary>
    /// The notifications on screen, the newest last. Changed on the UI thread only.
    /// </summary>
    ReadOnlyObservableCollection<ToastVM> Notifications { get; }

    /// <summary>
    /// Everything shown since the application started, the newest first: what the notification center lists.
    /// Changed on the UI thread only.
    /// </summary>
    ReadOnlyObservableCollection<ToastVM> History { get; }

    bool HasHistory { get; }

    /// <summary>
    /// Whether something has been shown since the notification center was last looked at.
    /// </summary>
    bool HasUnread { get; }

    /// <summary>
    /// Whether notifications come silently. The service plays nothing itself: the platform that
    /// makes a sound for a notification asks this first.
    /// </summary>
    bool IsSoundMuted { get; set; }

    /// <summary>
    /// The notification center has been looked at. Called on the UI thread.
    /// </summary>
    void MarkAllRead();

    /// <summary>
    /// Takes one notification out of the history. Called on the UI thread.
    /// </summary>
    void RemoveFromHistory(ToastVM notification);

    /// <summary>
    /// Empties the history; what is on screen stays until its time is out. Called on the UI thread.
    /// </summary>
    void ClearHistory();

    /// <summary>
    /// Shows a notification; may be called from any thread.
    /// </summary>
    /// <param name="title">The text of the notification, already in the language of the interface.</param>
    /// <param name="message">A second, quieter line with details.</param>
    /// <param name="lifetime">
    /// How long it stays; by default errors and warnings stay longer than the rest.
    /// <see cref="TimeSpan.Zero"/> keeps it until the user closes it.
    /// </param>
    void Show(NotificationType type, string title, string? message = null, TimeSpan? lifetime = null);
}
