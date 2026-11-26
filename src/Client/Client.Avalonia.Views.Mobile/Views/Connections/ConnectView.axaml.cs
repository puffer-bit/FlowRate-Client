using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.Mobile.Platform;
using Client.ViewModels.Connections;

namespace Client.Mobile.Views.Connections;

public partial class ConnectView : UserControl
{
    public ConnectView()
    {
        InitializeComponent();

        // Once the user is done with the address, a port typed into it moves to the port field
        AddressInput.LostFocus += (_, _) => (DataContext as ConnectWindowVM)?.NormalizeAddress();
    }

    private async void OnScanClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ConnectWindowVM viewModel && MobilePlatform.Current is { } platform)
            await viewModel.ScanAsync(platform.ScanQrCodeAsync);
    }
}
