using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Client.ViewModels.Sessions.List;

namespace Client.Views.Sessions.List;

public partial class HybridSessionPeerFlyout : UserControl
{
    public HybridSessionPeerFlyout()
    {
        InitializeComponent();
    }

    // The card opens from the peer once its menu has closed, the way the AFK card opens from its button
    private void OnPokeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PeerVM peer || this.FindLogicalAncestorOfType<Popup>()?.PlacementTarget is not { } target)
            return;

        Dispatcher.UIThread.Post(() => new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = new PokeCard { DataContext = peer }
        }.ShowAt(target), DispatcherPriority.Background);
    }
}
