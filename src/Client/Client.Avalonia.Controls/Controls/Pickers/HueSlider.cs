using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Client.Avalonia.Controls.Pickers;

/// <summary>
/// The rainbow strip of a color picker: the hue, from red through the spectrum back to red.
/// </summary>
public class HueSlider : Control
{
    private const double MaxHue = 360;
    // How far an arrow key moves the thumb, in degrees
    private const double KeyStep = 3;

    /// <summary>In degrees from 0 to 360; both ends are red.</summary>
    public static readonly StyledProperty<double> HueProperty =
        AvaloniaProperty.Register<HueSlider, double>(nameof(Hue),
            defaultBindingMode: BindingMode.TwoWay, coerce: (_, value) => Math.Clamp(value, 0, MaxHue));

    public static readonly StyledProperty<IBrush?> ThumbRingBrushProperty =
        ColorSquare.ThumbRingBrushProperty.AddOwner<HueSlider>();

    public static readonly StyledProperty<IBrush?> ThumbOutlineBrushProperty =
        ColorSquare.ThumbOutlineBrushProperty.AddOwner<HueSlider>();

    private static readonly IBrush Spectrum = CreateSpectrum();

    static HueSlider()
    {
        AffectsRender<HueSlider>(HueProperty, ThumbRingBrushProperty, ThumbOutlineBrushProperty);
        FocusableProperty.OverrideDefaultValue<HueSlider>(true);
    }

    public double Hue
    {
        get => GetValue(HueProperty);
        set => SetValue(HueProperty, value);
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

        // A strip with round ends; the thumb is taller than it and runs from end to end
        var radius = size.Height / 2;
        context.DrawRectangle(Spectrum, null, new RoundedRect(new Rect(size), radius));

        var center = new Point(Hue / MaxHue * size.Width, radius);
        ColorThumb.Draw(context, center, new HsvColor(1, Hue, 1, 1).ToRgb(), ThumbRingBrush, ThumbOutlineBrush);
    }

    // The pure hues every 60 degrees; between them the channels change evenly, as the gradient does
    private static IBrush CreateSpectrum()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative)
        };

        for (var hue = 0; hue <= MaxHue; hue += 60)
            brush.GradientStops.Add(new GradientStop(new HsvColor(1, hue, 1, 1).ToRgb(), hue / MaxHue));

        return brush.ToImmutable();
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
                SetCurrentValue(HueProperty, Hue - KeyStep);
                break;
            case Key.Right:
                SetCurrentValue(HueProperty, Hue + KeyStep);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void MoveThumb(Point position)
    {
        var width = Bounds.Width;
        if (width > 0)
            SetCurrentValue(HueProperty, position.X / width * MaxHue);
    }
}
