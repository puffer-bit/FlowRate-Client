using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.Views.Sessions.Hybrid;

namespace Client.Views.Main;

/// <summary>
/// The hybrid session the user is in, shown in the side panel of the main window while other content
/// is open.
/// </summary>
internal partial class HybridSessionPanel : UserControl
{
    private void OnSessionLogs(object? sender, RoutedEventArgs e)
    {
        if (sender is HybridSessionStatsFlyout { Session: { } session }
            && TopLevel.GetTopLevel(this) is Window owner)
            HybridSessionLogsWindow.Open(owner, session);
    }

    public HybridSessionPanel()
    {
        InitializeComponent();
    }
}
