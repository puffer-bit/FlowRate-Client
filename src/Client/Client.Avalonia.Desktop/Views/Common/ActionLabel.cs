using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Client.Views.Common;

/// <summary>The shared icon and text arrangement inside a dialog action.</summary>
public class ActionLabel : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ActionLabel, string?>(nameof(Text));
    public static readonly StyledProperty<IImage?> IconProperty =
        AvaloniaProperty.Register<ActionLabel, IImage?>(nameof(Icon));

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public IImage? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
}
