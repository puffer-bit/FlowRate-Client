using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Client.Views.Dialogs;

/// <summary>A dialog title and optional subtitle, with room for status indicators.</summary>
public class DialogHeader : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<DialogHeader, string?>(nameof(Title));
    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<DialogHeader, string?>(nameof(Subtitle));
    public static readonly StyledProperty<IImage?> IconProperty =
        AvaloniaProperty.Register<DialogHeader, IImage?>(nameof(Icon));
    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<DialogHeader, ICommand?>(nameof(CloseCommand));

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public IImage? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public ICommand? CloseCommand { get => GetValue(CloseCommandProperty); set => SetValue(CloseCommandProperty, value); }
}
