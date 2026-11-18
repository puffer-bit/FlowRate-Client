using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// Opens a link in the browser when the control is tapped or clicked, after the confirmation agreed,
/// as a link in the text of a message does.
/// </summary>
public static class ChatLinkOpener
{
    public static readonly AttachedProperty<ChatLinkPreviewVM?> PreviewProperty =
        AvaloniaProperty.RegisterAttached<Control, ChatLinkPreviewVM?>("Preview", typeof(ChatLinkOpener));

    public static ChatLinkPreviewVM? GetPreview(Control control) => control.GetValue(PreviewProperty);
    public static void SetPreview(Control control, ChatLinkPreviewVM? value) => control.SetValue(PreviewProperty, value);

    static ChatLinkOpener()
    {
        PreviewProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            var (previous, current) = e.GetOldAndNewValue<ChatLinkPreviewVM?>();
            if (previous == null && current != null)
            {
                control.Tapped += OnTapped;
                control.Cursor = new Cursor(StandardCursorType.Hand);
            }
            else if (previous != null && current == null)
            {
                control.Tapped -= OnTapped;
                control.ClearValue(InputElement.CursorProperty);
            }
        });
    }

    private static void OnTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control control || GetPreview(control) is not { Url: { } url } preview)
            return;

        e.Handled = true;
        _ = OpenAsync(control, url, preview.ConfirmOpen);
    }

    private static async Task OpenAsync(Control control, Uri url, Func<Uri, Task<bool>>? confirm)
    {
        if (confirm != null && !await confirm(url))
            return;

        if (TopLevel.GetTopLevel(control) is { } topLevel)
            await topLevel.Launcher.LaunchUriAsync(url);
    }
}
