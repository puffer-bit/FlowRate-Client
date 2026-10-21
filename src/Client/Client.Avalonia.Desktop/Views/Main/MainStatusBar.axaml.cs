using Avalonia.Controls;

namespace Client.Views.Main;

/// <summary>
/// The strip under the content of the main window: what the application is doing, the ping and
/// the notification center.
/// </summary>
internal partial class MainStatusBar : UserControl
{
    public MainStatusBar()
    {
        InitializeComponent();
    }
}
