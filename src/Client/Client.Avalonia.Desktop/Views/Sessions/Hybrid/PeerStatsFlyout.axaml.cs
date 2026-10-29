using Avalonia.Controls;

namespace Client.Views.Sessions.Hybrid;

/// <summary>
/// How the streams of one user reach us, with graphs of their loss and jitter.
/// </summary>
public partial class PeerStatsFlyout : UserControl
{
    public PeerStatsFlyout()
    {
        InitializeComponent();
    }
}
