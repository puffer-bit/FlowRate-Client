using Avalonia.Controls;

namespace Client.Views.Main;

/// <summary>
/// The strip under the content of the main window: the tasks in progress, the ping and
/// the notification center.
/// </summary>
internal partial class MainStatusBar : UserControl
{
    public MainStatusBar()
    {
        InitializeComponent();
    }
}
