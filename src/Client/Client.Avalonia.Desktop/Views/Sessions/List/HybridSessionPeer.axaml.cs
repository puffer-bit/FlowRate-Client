using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Client.Views.Sessions.List;

public partial class HybridSessionPeer : UserControl
{
    public HybridSessionPeer()
    {
        InitializeComponent();

        Tapped += OnTapped;
        if (ContextFlyout is { } flyout)
            flyout.Closed += OnMenuClosed;
    }

    // A click on the peer opens its menu; the buttons on the row keep their own clicks
    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) != null)
            return;

        ContextFlyout?.ShowAt(this);
        e.Handled = true;
    }

    // The peer stays selected only while its menu is open
    private void OnMenuClosed(object? sender, EventArgs e)
    {
        if (this.FindAncestorOfType<ListBox>() is { } list && list.SelectedItem == DataContext)
            list.SelectedItem = null;
    }
}
