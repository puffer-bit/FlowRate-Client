using Avalonia.Controls;
using Client.ViewModels.Sessions.Chat;

namespace Client.Views.Shared.Chat;

public partial class ChatLinkPreviewCard : UserControl
{
    public ChatLinkPreviewCard()
    {
        InitializeComponent();
    }

    // A card that can be opened takes clicks; the one above the input only shows what the message will carry
    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        var preview = DataContext as ChatLinkPreviewVM;
        ChatLinkOpener.SetPreview(this, preview?.ConfirmOpen != null ? preview : null);
        Classes.Set("openable", preview?.ConfirmOpen != null);
    }
}
