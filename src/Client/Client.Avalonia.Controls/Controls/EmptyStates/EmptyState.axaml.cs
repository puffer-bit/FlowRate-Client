using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Client.Avalonia.Controls.EmptyStates;

/// <summary>
/// What a place shows while it has no content of its own: a large icon, a title and a line of text
/// under it, centered in the room it is given.
/// </summary>
public class EmptyState : TemplatedControl
{
    public static readonly StyledProperty<IImage?> IconProperty =
        AvaloniaProperty.Register<EmptyState, IImage?>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Title));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Text));

    public IImage? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
