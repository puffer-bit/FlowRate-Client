using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Client.Avalonia.Controls.Pickers;

/// <summary>
/// The square of a color picker: every saturation, left to right, and every brightness, bottom to
/// top, of one hue. The thumb is dragged with the pointer or moved with the arrow keys.
/// </summary>
public class ColorSquare : Control
{
    // How far an arrow key moves the thumb, as a share of the square
    private const double KeyStep = 0.02;

    /// <summary>The hue the square shows, in degrees from 0 to 360.</summary>
    public static readonly StyledProperty<double> HueProperty =
        AvaloniaProperty.Register<ColorSquare, double>(nameof(Hue));

    /// <summary>From 0, gray, to 1, the pure color.</summary>
    public static readonly StyledProperty<double> SaturationProperty =
        AvaloniaProperty.Register<ColorSquare, double>(nameof(Saturation), 1,
            defaultBindingMode: BindingMode.TwoWay, coerce: (_, value) => Math.Clamp(value, 0, 1));

    /// <summary>From 0, black, to 1, the full brightness.</summary>
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ColorSquare, double>(nameof(Value), 1,
            defaultBindingMode: BindingMode.TwoWay, coerce: (_, value) => Math.Clamp(value, 0, 1));

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        Border.CornerRadiusProperty.AddOwner<ColorSquare>();

    public static readonly StyledProperty<IBrush?> ThumbRingBrushProperty =
        AvaloniaProperty.Register<ColorSquare, IBrush?>(nameof(ThumbRingBrush));

    public static readonly StyledProperty<IBrush?> ThumbOutlineBrushProperty =
        AvaloniaProperty.Register<ColorSquare, IBrush?>(nameof(ThumbOutlineBrush));

    static ColorSquare()
    {
        AffectsRender<ColorSquare>(HueProperty, SaturationProperty, ValueProperty, CornerRadiusProperty,
            ThumbRingBrushProperty, ThumbOutlineBrushProperty);
        FocusableProperty.OverrideDefaultValue<ColorSquare>(true);
    }

    public double Hue
    {
        get => GetValue(HueProperty);
        set => SetValue(HueProperty, value);
    }

    public double Saturation
    {
        get => GetValue(SaturationProperty);
        set => SetValue(SaturationProperty, value);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public IBrush? ThumbRingBrush
    {
        get => GetValue(ThumbRingBrushProperty);
        set => SetValue(ThumbRingBrushProperty, value);
    }

    public IBrush? ThumbOutlineBrush
    {
        get => GetValue(ThumbOutlineBrushProperty);
        set => SetValue(ThumbOutlineBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        var area = new RoundedRect(new Rect(size), CornerRadius);
        var pure = new HsvColor(1, Hue, 1, 1).ToRgb();

        // White to the pure color across, then black laid over it from the bottom: the two axes of
        // the color model, not colors of the interface
        context.DrawRectangle(Gradient(Colors.White, pure, new RelativePoint(1, 0, RelativeUnit.Relative)), null, area);
        context.DrawRectangle(Gradient(Color.FromArgb(0, 0, 0, 0), Colors.Black, new RelativePoint(0, 1, RelativeUnit.Relative)),
            null, area);

        var center = new Point(Saturation * size.Width, (1 - Value) * size.Height);
        ColorThumb.Draw(context, center, new HsvColor(1, Hue, Saturation, Value).ToRgb(), ThumbRingBrush, ThumbOutlineBrush);
    }

    private static LinearGradientBrush Gradient(Color from, Color to, RelativePoint end)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = end,
            GradientStops = { new GradientStop(from, 0), new GradientStop(to, 1) }
        };
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        Focus();
        e.Pointer.Capture(this);
        MoveThumb(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (ReferenceEquals(e.Pointer.Captured, this))
            MoveThumb(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (ReferenceEquals(e.Pointer.Captured, this))
            e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Left:
                SetCurrentValue(SaturationProperty, Saturation - KeyStep);
                break;
            case Key.Right:
                SetCurrentValue(SaturationProperty, Saturation + KeyStep);
                break;
            case Key.Up:
                SetCurrentValue(ValueProperty, Value + KeyStep);
                break;
            case Key.Down:
                SetCurrentValue(ValueProperty, Value - KeyStep);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    // A pointer dragged past an edge keeps the thumb on that edge
    private void MoveThumb(Point position)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        SetCurrentValue(SaturationProperty, position.X / size.Width);
        SetCurrentValue(ValueProperty, 1 - position.Y / size.Height);
    }
}
