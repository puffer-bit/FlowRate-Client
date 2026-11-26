using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.ViewModels.Sessions.Chat;

namespace Client.Views.Shared.Chat;

public partial class ChatAppLinkCard : UserControl
{
    public ChatAppLinkCard()
    {
        InitializeComponent();
    }

    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        (DataContext as ChatAppLinkVM)?.Open();
    }
}
