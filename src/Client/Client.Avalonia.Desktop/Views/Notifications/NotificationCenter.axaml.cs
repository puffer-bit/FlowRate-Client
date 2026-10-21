using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.ViewModels.Notifications;

namespace Client.Views.Notifications;

public partial class NotificationCenter : UserControl
{
    public NotificationCenter()
    {
        InitializeComponent();
    }

    // Whatever came before the center was opened, and while it was open, has been seen
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
