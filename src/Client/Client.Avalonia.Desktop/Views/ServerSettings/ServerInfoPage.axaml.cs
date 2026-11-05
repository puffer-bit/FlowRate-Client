using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.ViewModels.ServerSettings;
using Client.Views.Common;

namespace Client.Views.ServerSettings;

public partial class ServerInfoPage : UserControl
{
    public ServerInfoPage()
    {
        InitializeComponent();
    }

    private async void CopyAddress_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ServerInfoTabVM { Address: { } address })
            await ClipboardAccess.SetTextAsync(this, address);
    }
}
