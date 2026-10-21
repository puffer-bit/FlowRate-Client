using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Metadata;

namespace Client.Avalonia.Controls.Hints;

/// <summary>
/// A hint under a setting or in a dialog: the blue info icon followed by small italic text.
/// The text can be set with <see cref="Text"/> or written as the element content.
/// </summary>
public class HintBlock : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<HintBlock, string?>(nameof(Text));

    [Content]
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
