using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.ViewModels.Notifications;

namespace Client.Droid.Views.Notifications;

/// <summary>
/// The notification center of the desktop as a page of its own: everything shown at the top of the
/// screen before, with the sound and the clearing of the list in the menu of the header.
/// </summary>
public partial class NotificationsPage : UserControl
{
    public NotificationsPage()
    {
        InitializeComponent();
    }

    public event Action? CloseRequested;

    private void OnBackClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    // Whatever came before the page was opened, and while it was open, has been seen
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        (DataContext as NotificationCenterVM)?.MarkAllRead();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        (DataContext as NotificationCenterVM)?.MarkAllRead();
        base.OnUnloaded(e);
    }
}
