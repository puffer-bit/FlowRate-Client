using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.Mobile.Platform;
using Client.ViewModels.Main;
using Client.Views.Shared.Dialogs;

namespace Client.Mobile.Views.Dialogs;

/// <summary>
/// The QR code of the server we are connected to, with a button that sends the link through the share sheet.
/// </summary>
public partial class ServerLinkDialog : UserControl
{
    public ServerLinkDialog()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => DialogPresenter.Current?.Close(this);

    private void OnShareClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ServerTitleVM { ServerLink: { } link } title)
            MobilePlatform.Current?.ShareText(link, title.DisplayName);
    }
}
