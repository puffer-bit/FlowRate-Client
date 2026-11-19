using Avalonia;
using Avalonia.Controls;

namespace Client.Avalonia.Controls.Images;

/// <summary>Fits media to the available column while preserving its natural proportions.</summary>
public sealed class MediaFrame : Decorator
{
    public static readonly StyledProperty<double> MediaWidthProperty =
        AvaloniaProperty.Register<MediaFrame, double>(nameof(MediaWidth), 400);
    public static readonly StyledProperty<double> MediaHeightProperty =
        AvaloniaProperty.Register<MediaFrame, double>(nameof(MediaHeight), 225);
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<MediaFrame, bool>(nameof(IsExpanded));

    static MediaFrame() => AffectsMeasure<MediaFrame>(MediaWidthProperty, MediaHeightProperty, IsExpandedProperty);

    public double MediaWidth { get => GetValue(MediaWidthProperty); set => SetValue(MediaWidthProperty, value); }
    public double MediaHeight { get => GetValue(MediaHeightProperty); set => SetValue(MediaHeightProperty, value); }
    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (IsExpanded)
            return base.MeasureOverride(availableSize);

        var size = Fit(availableSize);
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (IsExpanded)
            return base.ArrangeOverride(finalSize);

        var size = Fit(finalSize);
        Child?.Arrange(new Rect((finalSize.Width - size.Width) / 2, 0, size.Width, size.Height));
        return finalSize;
    }

    private Size Fit(Size availableSize)
    {
        double width = double.IsFinite(MediaWidth) && MediaWidth > 0 ? MediaWidth : 400;
        double height = double.IsFinite(MediaHeight) && MediaHeight > 0 ? MediaHeight : 225;
        double scale = System.Math.Min(1, System.Math.Min(availableSize.Width / width, availableSize.Height / height));
        return new Size(width * scale, height * scale);
    }
}
