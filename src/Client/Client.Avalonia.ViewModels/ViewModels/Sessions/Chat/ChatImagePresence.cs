using Avalonia;
using Avalonia.Controls;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// Tells a <see cref="ChatImageVM"/> while a control shows it, so the image loads when its tile
/// comes into the visual tree and lets go of the bitmap when the tile leaves it.
/// </summary>
public static class ChatImagePresence
{
    public static readonly AttachedProperty<ChatImageVM?> ImageProperty =
        AvaloniaProperty.RegisterAttached<Control, ChatImageVM?>("Image", typeof(ChatImagePresence));

    public static ChatImageVM? GetImage(Control control) => control.GetValue(ImageProperty);
    public static void SetImage(Control control, ChatImageVM? value) => control.SetValue(ImageProperty, value);

    static ChatImagePresence()
    {
        ImageProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            var (previous, current) = e.GetOldAndNewValue<ChatImageVM?>();

            if (previous == null && current != null)
            {
                control.AttachedToVisualTree += OnAttached;
                control.DetachedFromVisualTree += OnDetached;
            }
            else if (previous != null && current == null)
            {
                control.AttachedToVisualTree -= OnAttached;
                control.DetachedFromVisualTree -= OnDetached;
            }

            // A recycled tile is given another image while it stays in the tree
            if (TopLevel.GetTopLevel(control) != null)
            {
                previous?.Hide();
                current?.Show();
            }
        });
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) =>
        GetImage((Control)sender!)?.Show();

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) =>
        GetImage((Control)sender!)?.Hide();
}
