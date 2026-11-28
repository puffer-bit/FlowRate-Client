using Avalonia.Controls;

namespace Client.Views.Main;

/// <summary>
/// The list behind the task segment of the status bar: what uploads, downloads and compresses, how far
/// it got, and a cancel for what can be stopped.
/// </summary>
public partial class TasksFlyout : UserControl
{
    public TasksFlyout()
    {
        InitializeComponent();
    }
}
