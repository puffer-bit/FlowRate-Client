using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Client.Avalonia.Controls.Images;

/// <summary>
/// Lets what it holds be zoomed in, as <see cref="ZoomableImage"/> does with a picture: with two fingers
/// spread apart, a double tap or the wheel, and dragged around while it is zoomed. The child keeps
/// drawing itself at its own size; the zoom is a transform of it, so any control can be held, such as
/// the picture of a shared screen.
/// </summary>
/// <remarks>
/// While nothing is zoomed a single finger is left alone, so taps and scrolling reach what lies under it.
/// </remarks>
public class ZoomDecorator : Decorator
{
    private const double WheelStep = 1.2;
    private const double DoubleTapZoom = 2.5;
    private const double MaxZoom = 6;

    // The pointers pressed on the control, where they are now
    private readonly Dictionary<IPointer, Point> _pointers = new();
    // 1 is the child as it is laid out
    private double _zoom = 1;
    // Where the top left corner of the zoomed child is, in the coordinates of this control
    private Vector _offset;

    static ZoomDecorator()
    {
        ClipToBoundsProperty.OverrideDefaultValue<ZoomDecorator>(true);
        // The control takes the pointer over all of it, also where the child draws nothing
        BackgroundProperty.OverrideDefaultValue<ZoomDecorator>(Brushes.Transparent);
    }

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<ZoomDecorator>();

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public bool IsZoomed => _zoom > 1;

    /// <summary>Back to the child as it is laid out.</summary>
    public void ResetZoom()
    {
        _zoom = 1;
        _offset = default;
        UpdateTransform();
    }

    public override void Render(DrawingContext context)
    {
        if (Background is { } background)
            context.FillRectangle(background, new Rect(Bounds.Size));
        base.Render(context);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        // The offset that fit the old size may leave a gap at an edge of the new one
        _offset = ClampOffset(_offset, size);
        UpdateTransform();
        return size;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y == 0)
            return;

        ZoomAt(e.GetPosition(this), _zoom * Math.Pow(WheelStep, e.Delta.Y));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        if (e.Pointer.Type == PointerType.Mouse && !point.Properties.IsLeftButtonPressed)
            return;

        _pointers[e.Pointer] = point.Position;

        // One finger on what is not zoomed is a tap for the child; a second one starts a pinch
        if (_pointers.Count < 2 && !IsZoomed)
            return;

        foreach (var pointer in _pointers.Keys)
            pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_pointers.TryGetValue(e.Pointer, out var previous))
            return;

        var position = e.GetPosition(this);
        _pointers[e.Pointer] = position;

        if (_pointers.Count == 1)
        {
            if (!IsZoomed)
                return;

            MoveBy(position - previous);
        }
        else
        {
            // Two fingers: the child is pinched about the point between them and follows that point
            var other = _pointers.First(pair => !ReferenceEquals(pair.Key, e.Pointer)).Value;
            var before = Point.Distance(previous, other);
            var middleBefore = (previous + other) / 2;
            var middle = (position + other) / 2;

            if (before > 0)
                ZoomAt(middleBefore, _zoom * Point.Distance(position, other) / before);
            MoveBy(middle - middleBefore);
        }

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_pointers.Remove(e.Pointer))
            return;

        if (ReferenceEquals(e.Pointer.Captured, this))
        {
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _pointers.Remove(e.Pointer);
    }

    protected override void OnDoubleTapped(TappedEventArgs e)
    {
        base.OnDoubleTapped(e);

        if (IsZoomed)
            ResetZoom();
        else
            ZoomAt(e.GetPosition(this), DoubleTapZoom);
        e.Handled = true;
    }

    private void ZoomAt(Point point, double zoom)
    {
        zoom = Math.Clamp(zoom, 1, MaxZoom);
        if (zoom == _zoom)
            return;

        // The point of the child under the fingers stays under them
        var offset = (Vector)point - ((Vector)point - _offset) * (zoom / _zoom);
        _zoom = zoom;
        _offset = ClampOffset(offset, Bounds.Size);
        UpdateTransform();
    }

    private void MoveBy(Vector delta)
    {
        var offset = ClampOffset(_offset + delta, Bounds.Size);
        if (offset == _offset)
            return;

        _offset = offset;
        UpdateTransform();
    }

    // The zoomed child always covers the whole control: no gap opens at an edge
    private Vector ClampOffset(Vector offset, Size size) => new(
        Math.Clamp(offset.X, size.Width - size.Width * _zoom, 0),
        Math.Clamp(offset.Y, size.Height - size.Height * _zoom, 0));

    private void UpdateTransform()
    {
        if (Child is not { } child)
            return;

        child.RenderTransformOrigin = RelativePoint.TopLeft;
        child.RenderTransform = IsZoomed
            ? new MatrixTransform(Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_offset.X, _offset.Y))
            : null;
    }
}
