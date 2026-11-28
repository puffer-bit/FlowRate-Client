using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.Views.Common;
using Client.ViewModels.Main;

namespace Client.Views.Main;

public partial class ServerTitleFlyout : UserControl
{
    public ServerTitleFlyout()
    {
        InitializeComponent();
    }

    private async void CopyAddress_Click(object? sender, RoutedEventArgs e)
    {
        // The menu closes on this click and takes its data context away, so the address is read first.
        // The clipboard is taken from the main window: the popup of the menu is on its way out
        if ((DataContext as ServerTitleVM)?.Address is { } address)
            await ClipboardAccess.SetTextAsync(null, address);
    }

    private async void CopyLink_Click(object? sender, RoutedEventArgs e)
    {
        if ((DataContext as ServerTitleVM)?.ServerLink is { } link)
            await ClipboardAccess.SetTextAsync(null, link);
    }
}
