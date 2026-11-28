using Avalonia.Controls;
using Client.Views.Common;
using Avalonia.Markup.Xaml;
using Client.ViewModels.Sessions.Chat;

namespace Client.Views.Sessions.Chat;

public partial class ChatDateSeparatorControl : ViewControl<ChatDateSeparatorVM>
{
    public ChatDateSeparatorControl()
    {
        InitializeComponent();
    }
}