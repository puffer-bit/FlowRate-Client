using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;

namespace Client.Avalonia.Controls.Images;

/// <summary>
/// An image fitted into the control, never enlarged, that can be zoomed in: with the wheel, a
/// double click or tap, or two fingers spread apart, and dragged around while it is zoomed.
/// </summary>
/// <remarks>
/// Only the picture takes the pointer, so a click beside it reaches what lies under the control.
/// </remarks>
public class ZoomableImage : Control, ICustomHitTest
{
    // How much one notch of the wheel zooms
    private const double WheelStep = 1.2;
    // A double click zooms to at least this, or to the pixels of the picture if they are smaller
    private const double DoubleTapZoom = 2.5;
    // The zoom goes as far as this, or until a pixel of the picture covers this many of the screen
    private const double MinMaxZoom = 4;
    private const double MaxPixelScale = 8;

    private static readonly Cursor GrabCursor = new(StandardCursorType.Hand);
    private static readonly Cursor DragCursor = new(StandardCursorType.SizeAll);

    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<ZoomableImage, IImage?>(nameof(Source));

    /// <summary>Kept clear around the fitted picture; a zoomed one goes up to the edges.</summary>
    public static readonly StyledProperty<Thickness> PaddingProperty =
        Decorator.PaddingProperty.AddOwner<ZoomableImage>();

    // The pointers pressed on the picture, where they are now
    private readonly Dictionary<IPointer, Point> _pointers = new();
    // 1 is the fitted picture
    private double _zoom = 1;
    // Of the center of the picture from the center of the control
    private Vector _offset;

    static ZoomableImage()
    {
        AffectsRender<ZoomableImage>(SourceProperty, PaddingProperty);
        AffectsMeasure<ZoomableImage>(SourceProperty, PaddingProperty);
        ClipToBoundsProperty.OverrideDefaultValue<ZoomableImage>(true);
    }

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Thickness Padding
    {
        get => GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    public bool IsZoomed => _zoom > 1;

    /// <summary>Back to the fitted picture.</summary>
    public void ResetZoom()
    {
        _zoom = 1;
        _offset = default;
        UpdateCursor();
        InvalidateVisual();
    }

    public bool HitTest(Point point) =>
        _pointers.Count > 0 || (Source != null && ShownRect().Intersect(new Rect(Bounds.Size)).Contains(point));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Another picture, the image itself in place of its reduced copy, starts fitted again
        if (change.Property == SourceProperty)
            ResetZoom();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Source is not { } source)
            return default;

        // Fills what it is given, so the zoomed picture has all of it
        var width = double.IsInfinity(availableSize.Width) ? source.Size.Width : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? source.Size.Height : availableSize.Height;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // The offset that fit the old size may leave a gap at an edge of the new one
        _offset = ClampOffset(_offset, finalSize);
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        if (Source is not { } source || source.Size.Width <= 0 || source.Size.Height <= 0)
            return;

        context.DrawImage(source, new Rect(source.Size), ShownRect());
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Source == null || e.Delta.Y == 0)
            return;

        ZoomAt(e.GetPosition(this), _zoom * Math.Pow(WheelStep, e.Delta.Y));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Source == null)
            return;

        var point = e.GetCurrentPoint(this);
        if (e.Pointer.Type == PointerType.Mouse && !point.Properties.IsLeftButtonPressed)
            return;

        _pointers[e.Pointer] = point.Position;
        e.Pointer.Capture(this);
        UpdateCursor();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_pointers.TryGetValue(e.Pointer, out var previous))
            return;

        var position = e.GetPosition(this);
        if (_pointers.Count == 1)
        {
            _pointers[e.Pointer] = position;
            MoveBy(position - previous);
        }
        else
        {
            // Two fingers: the picture is pinched about the point between them and follows that point
            var other = _pointers.First(pair => !ReferenceEquals(pair.Key, e.Pointer)).Value;
            var before = Point.Distance(previous, other);
            var middleBefore = (previous + other) / 2;
            _pointers[e.Pointer] = position;
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

        e.Pointer.Capture(null);
        UpdateCursor();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _pointers.Remove(e.Pointer);
        UpdateCursor();
    }

    protected override void OnDoubleTapped(TappedEventArgs e)
    {
        base.OnDoubleTapped(e);
        if (Source == null)
            return;

        if (IsZoomed)
            ResetZoom();
        else
            ZoomAt(e.GetPosition(this), Math.Max(DoubleTapZoom, 1 / FitScale(Bounds.Size)));
        e.Handled = true;
    }

    private void ZoomAt(Point point, double zoom)
    {
        zoom = Math.Clamp(zoom, 1, MaxZoom());
        if (zoom == _zoom)
            return;

        // The point of the picture under the pointer stays under it
        var center = Center(Bounds.Size) + _offset;
        var newCenter = point - (point - center) * (zoom / _zoom);
        _zoom = zoom;
        _offset = ClampOffset(newCenter - Center(Bounds.Size), Bounds.Size);
        UpdateCursor();
        InvalidateVisual();
    }

    private void MoveBy(Vector delta)
    {
        var offset = ClampOffset(_offset + delta, Bounds.Size);
        if (offset == _offset)
            return;

        _offset = offset;
        InvalidateVisual();
    }

    // A side of the picture smaller than the control stays in the middle; a larger one is kept from
    // leaving a gap at either edge
    private Vector ClampOffset(Vector offset, Size size)
    {
        var shown = ShownSize(size);
        var spareX = Math.Max(0, (shown.Width - size.Width) / 2);
        var spareY = Math.Max(0, (shown.Height - size.Height) / 2);
        return new Vector(Math.Clamp(offset.X, -spareX, spareX), Math.Clamp(offset.Y, -spareY, spareY));
    }

    private Rect ShownRect()
    {
        var shown = ShownSize(Bounds.Size);
        var center = Center(Bounds.Size) + _offset;
        return new Rect(center.X - shown.Width / 2, center.Y - shown.Height / 2, shown.Width, shown.Height);
    }

    private Size ShownSize(Size size) =>
        Source is { } source ? source.Size * (FitScale(size) * _zoom) : default;

    // Of the fitted picture to the picture itself, at most 1
    private double FitScale(Size size)
    {
        if (Source is not { } source || source.Size.Width <= 0 || source.Size.Height <= 0)
            return 1;

        var room = size.Deflate(Padding);
        var scale = Math.Min(room.Width / source.Size.Width, room.Height / source.Size.Height);
        return scale > 0 ? Math.Min(scale, 1) : 1;
    }

    private double MaxZoom() => Math.Max(MinMaxZoom, MaxPixelScale / FitScale(Bounds.Size));

    private void UpdateCursor() =>
        Cursor = IsZoomed ? _pointers.Count > 0 ? DragCursor : GrabCursor : null;

    private static Point Center(Size size) => new(size.Width / 2, size.Height / 2);
}
