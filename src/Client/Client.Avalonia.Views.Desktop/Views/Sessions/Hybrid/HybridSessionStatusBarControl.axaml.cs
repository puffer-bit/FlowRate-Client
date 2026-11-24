using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.ViewModels.Sessions.Hybrid;

namespace Client.Views.Sessions.Hybrid;

public partial class HybridSessionStatusBarControl : UserControl
{
    private void OnSessionLogs(object? sender, RoutedEventArgs e)
    {
        if (DataContext is HybridSessionStatusBarVM session)
            HybridSessionLogsView.Open(this, session);
    }

    public HybridSessionStatusBarControl()
    {
        InitializeComponent();
    }
}