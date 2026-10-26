using Avalonia.Controls;

namespace Client.Views.Sessions.Hybrid;

/// <summary>
/// The quality of the connection of a hybrid session, with graphs of its round trip and its loss.
/// </summary>
public partial class HybridSessionStatsFlyout : UserControl
{
    public HybridSessionStatsFlyout()
    {
        InitializeComponent();
    }
}
