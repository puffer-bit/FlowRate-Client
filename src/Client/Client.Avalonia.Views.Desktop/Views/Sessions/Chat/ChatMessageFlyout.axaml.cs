using Avalonia.Controls;
using Avalonia.Input;
using Client.ViewModels.Sessions.Chat;

namespace Client.Views.Sessions.Chat;

public partial class ChatMessageFlyout : UserControl
{
    public ChatMessageFlyout()
    {
        InitializeComponent();
    }

    private void OnReaderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control row)
            ChatUserMenu.OpenForReader(row, e);
    }
}
