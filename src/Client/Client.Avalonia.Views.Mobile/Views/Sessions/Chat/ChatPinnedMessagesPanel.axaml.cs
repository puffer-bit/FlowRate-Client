using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.ViewModels.Sessions.Chat;

namespace Client.Mobile.Views.Sessions.Chat;

/// <summary>The pinned messages of a chat, as a page over the whole chat.</summary>
public partial class ChatPinnedMessagesPanel : UserControl
{
    public ChatPinnedMessagesPanel() => InitializeComponent();

    private void Close()
    {
        if (DataContext is ActiveChatSessionVM chat)
            chat.StatusBar.IsPinsOpen = false;
    }

    private void OnBackClick(object? sender, RoutedEventArgs e) => Close();

    private void OnGoToMessageClick(object? sender, RoutedEventArgs e) => Close();
}
