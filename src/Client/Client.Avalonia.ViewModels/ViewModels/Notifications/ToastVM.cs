using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Avalonia.Styles.Animation;
using Client.Services;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Notifications;

/// <summary>
/// One notification of <see cref="INotificationService"/> in the corner of the main window.
/// </summary>
public class ToastVM : ReactiveObject
{
    private readonly Action<ToastVM> _remove;
    private DispatcherTimer? _lifetimeTimer;

    public NotificationType Type { get; }

    public string Title { get; }

    public string? Message { get; }

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    /// <summary>
    /// When it was shown, as the notification center prints it.
    /// </summary>
    public string TimeText { get; } = DateTime.Now.ToString("HH:mm");

    // What the view picks the icon and the color of the stripe by; a kind the corner has no look for is shown as info
    public bool IsSuccess => Type == NotificationType.Success;
    public bool IsWarning => Type == NotificationType.Warning;
    public bool IsError => Type == NotificationType.Error;
    public bool IsInfo => !IsSuccess && !IsWarning && !IsError;

    /// <summary>
    /// The notification is playing its leave animation and is removed from the list right after it.
    /// </summary>
    public bool IsLeaving
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// Set by the view. A notification under the pointer is being read: its time stops,
    /// and starts over when the pointer leaves.
    /// </summary>
    public bool IsPointerOver
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;

            if (_lifetimeTimer == null || IsLeaving)
                return;

            // Stopping and starting a timer counts its whole interval again
            _lifetimeTimer.Stop();

            if (!value)
                _lifetimeTimer.Start();
        }
    }

    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }

    public ToastVM(NotificationType type, string title, string? message, Action<ToastVM> remove)
    {
        Type = type;
        Title = title;
        Message = message;
        _remove = remove;

        CloseCommand = ReactiveCommand.CreateFromTask(CloseAsync);
    }

    /// <summary>
    /// Closes the notification by itself after <paramref name="lifetime"/>. Called on the UI thread.
    /// </summary>
    public void StartLifetime(TimeSpan lifetime)
    {
        _lifetimeTimer = new DispatcherTimer { Interval = lifetime };
        _lifetimeTimer.Tick += (_, _) => _ = CloseAsync();

        if (!IsPointerOver)
            _lifetimeTimer.Start();
    }

    /// <summary>
    /// Closes the notification: by its button, by its timer or to make room. Later calls do nothing.
    /// </summary>
    public async Task CloseAsync()
    {
        if (IsLeaving)
            return;

        IsLeaving = true;
        _lifetimeTimer?.Stop();
        await Task.Delay(Presence.LeaveTime);
        _remove(this);
    }
}
