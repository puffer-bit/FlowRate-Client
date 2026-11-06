using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Client.Core.Chat;
using Client.Avalonia.Localization;

namespace Client.Avalonia.Controls.Text;

public partial class ChatFormattingMenu : UserControl
{
    public static readonly StyledProperty<TextBox?> TargetProperty =
        AvaloniaProperty.Register<ChatFormattingMenu, TextBox?>(nameof(Target));

    public TextBox? Target { get => GetValue(TargetProperty); set => SetValue(TargetProperty, value); }

    public ChatFormattingMenu() => InitializeComponent();

    private void OnFormat(object? sender, RoutedEventArgs e)
    {
        if (Target is { } target && sender is Button { Tag: string tag }
            && Enum.TryParse<ChatMarkdownFormat>(tag, out var format))
            ChatInputFormatting.Apply(target, format);
    }
}

/// <summary>Shared editing and keyboard behavior for desktop and Android chat composers.</summary>
public static class ChatInputFormatting
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("Enabled", typeof(ChatInputFormatting));

    public static bool GetEnabled(TextBox target) => target.GetValue(EnabledProperty);
    public static void SetEnabled(TextBox target, bool value) => target.SetValue(EnabledProperty, value);

    static ChatInputFormatting()
    {
        EnabledProperty.Changed.AddClassHandler<TextBox>((target, e) =>
        {
            if (e.GetNewValue<bool>()) target.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            else target.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        });
    }

    public static bool Apply(TextBox target, ChatMarkdownFormat format)
    {
        if (target.IsReadOnly || !target.IsEnabled) return false;
        string text = target.Text ?? "";
        var edit = ChatMarkdownEdit.Create(text, target.SelectionStart, target.SelectionEnd, format,
            Strings.Get("chat.format.link"));
        if (target.MaxLength > 0 && text.Length - (edit.End - edit.Start) + edit.Replacement.Length > target.MaxLength)
            return false;
        target.SelectionStart = edit.Start;
        target.SelectionEnd = edit.End;
        target.SelectedText = edit.Replacement;
        target.Focus();
        target.SelectionStart = edit.SelectionStart;
        target.SelectionEnd = edit.SelectionEnd;
        return true;
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox target || e.Handled) return;
        ChatMarkdownFormat? format = (e.KeyModifiers, e.Key) switch
        {
            (KeyModifiers.Control, Key.B) => ChatMarkdownFormat.Bold,
            (KeyModifiers.Control, Key.I) => ChatMarkdownFormat.Italic,
            (KeyModifiers.Control | KeyModifiers.Shift, Key.X) => ChatMarkdownFormat.Strike,
            (KeyModifiers.Control, Key.E) => ChatMarkdownFormat.Code,
            (KeyModifiers.Control | KeyModifiers.Shift, Key.E) => ChatMarkdownFormat.CodeBlock,
            (KeyModifiers.Control, Key.K) => ChatMarkdownFormat.Link,
            _ => null
        };
        if (format is { } value)
        {
            Apply(target, value);
            e.Handled = true;
        }
    }
}
