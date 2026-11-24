using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Client.Views.Common;

/// <summary>A list placeholder with an icon or progress ring and optional custom hint or action.</summary>
public class StatePanel : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<StatePanel, string?>(nameof(Title));
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<StatePanel, string?>(nameof(Text));
    public static readonly StyledProperty<IImage?> IconProperty =
        AvaloniaProperty.Register<StatePanel, IImage?>(nameof(Icon));
    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<StatePanel, bool>(nameof(IsBusy));

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public IImage? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public bool IsBusy { get => GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
}
