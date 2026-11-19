using Avalonia.Controls;
using Client.ViewModels.Sessions.Chat;

namespace Client.Views.Shared.Chat;

public partial class ChatLinkPreviewCard : UserControl
{
    public ChatLinkPreviewCard()
    {
        InitializeComponent();
    }

    private void OnImageTapped(object? sender, global::Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is not ChatLinkPreviewVM { IsImageLoaded: true } preview)
            return;
        e.Handled = true;
        LinkImageLightbox.Open(this, preview);
    }

    private void OnImageKeyDown(object? sender, global::Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key is not (global::Avalonia.Input.Key.Enter or global::Avalonia.Input.Key.Space)
            || DataContext is not ChatLinkPreviewVM { IsImageLoaded: true } preview)
            return;
        e.Handled = true;
        LinkImageLightbox.Open(this, preview);
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
