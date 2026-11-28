using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.ViewModels.Sessions.Hybrid;

namespace Client.Views.Sessions.Hybrid;

/// <summary>
/// The quality of the connection of a hybrid session, with graphs of its round trip and its loss.
/// </summary>
public partial class HybridSessionStatsFlyout : UserControl
{
    public HybridSessionStatusBarVM? Session => DataContext as HybridSessionStatusBarVM;

    public event EventHandler<RoutedEventArgs>? SessionLogsRequested;

    private void OnSessionLogs(object? sender, RoutedEventArgs e)
    {
        SessionLogsRequested?.Invoke(this, e);
    }

    public HybridSessionStatsFlyout()
    {
        InitializeComponent();
    }
}
