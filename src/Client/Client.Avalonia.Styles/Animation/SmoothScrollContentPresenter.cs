using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Client.Avalonia.Styles.Animation;

/// <summary>
/// The presenter of every <see cref="ScrollViewer"/>: a turn of the mouse wheel moves the content to its
/// new place over several frames instead of jumping there. Each turn adds to the place the content is
/// heading to, so fast turning covers the same distance as it does without the animation.
/// The middle button scrolls as it does in Windows: held and dragged, the content moves while the button is
/// down, faster the further the pointer is from where it was pressed; clicked, it keeps moving that way until
/// the next click, a turn of the wheel or Escape.
/// </summary>
public class SmoothScrollContentPresenter : ScrollContentPresenter
{
    // The distance of one wheel notch, the same as in ScrollContentPresenter
    private const double WheelStep = 50;
    // The part of the animation time after which the remaining distance has shrunk e times;
    // by the end of the whole time about 5% of the way is left
    private const double TimeConstantShare = 1.0 / 3;
    private const double FirstFrameSeconds = 1.0 / 60;
    private const double ArrivalDistance = 0.5;

    private Vector _target;
    // The offset set by the last frame: a difference from it means something else has scrolled the content
    private Vector _lastSetOffset;
    private TimeSpan? _lastFrameTime;
    private bool _isAnimating;

    // The pointer this close to where the middle button was pressed does not scroll
    private const double AutoScrollDeadZone = 12;
    // Pixels per second for each pixel the pointer is beyond the dead zone
    private const double AutoScrollSpeed = 8;

    private TopLevel? _autoScrollTopLevel;
    private Point _autoScrollOrigin;
    private Point _autoScrollPointer;
    // Left the dead zone while the button was down, so its release ends the scrolling
    private bool _isAutoScrollDragged;
    private TimeSpan? _lastAutoScrollFrameTime;

    public SmoothScrollContentPresenter()
    {
        // Scrolling by touch, which the Fluent template declares in markup. There the recognizer has to
        // name the presenter it lies in, and the IDE does not resolve such a name
        var recognizer = new ScrollGestureRecognizer
        {
            [!ScrollGestureRecognizer.CanHorizontallyScrollProperty] = this[!CanHorizontallyScrollProperty],
            [!ScrollGestureRecognizer.CanVerticallyScrollProperty] = this[!CanVerticallyScrollProperty],
            [!ScrollGestureRecognizer.IsScrollInertiaEnabledProperty] = this[!ScrollViewer.IsScrollInertiaEnabledProperty],
            [!ScrollGestureRecognizer.OffsetProperty] = this[!OffsetProperty],
            [!ScrollGestureRecognizer.ViewportProperty] = this[!ViewportProperty],
            [!ScrollGestureRecognizer.ExtentProperty] = this[!ExtentProperty]
        };

        GestureRecognizers.Add(recognizer);
    }

    // Styles written for ScrollContentPresenter apply to this presenter too
    protected override Type StyleKeyOverride => typeof(ScrollContentPresenter);

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);

        // Logical scrolling counts in items and snap points pick the place themselves: both stay with the base class
        if (topLevel == null
            || Motion.Get(MotionDuration.Medium) <= TimeSpan.Zero
            || (Child is ILogicalScrollable { IsLogicalScrollEnabled: true } && !IsScrolledInPixels(Child))
            || HorizontalSnapPointsType != SnapPointsType.None
            || VerticalSnapPointsType != SnapPointsType.None)
        {
            _isAnimating = false;
            base.OnPointerWheelChanged(e);
            return;
        }

        if (Extent.Height <= Viewport.Height && Extent.Width <= Viewport.Width)
            return;

        var delta = e.Delta;

        // Shift turns the wheel sideways; not every platform does this itself
        if (e.KeyModifiers == KeyModifiers.Shift && Math.Abs(delta.X) < double.Epsilon)
            delta = new Vector(delta.Y, delta.X);
        else if (FlowDirection == FlowDirection.RightToLeft)
            delta = new Vector(-delta.X, delta.Y);

        var from = _isAnimating ? _target : Offset;
        var x = from.X;
        var y = from.Y;

        if (Extent.Height > Viewport.Height)
            y -= delta.Y * WheelStep;
        if (Extent.Width > Viewport.Width)
            x -= delta.X * WheelStep;

        var target = ClampToExtent(new Vector(x, y));
        var targetChanged = target != from;

        e.Handled = !IsScrollChainingEnabled || targetChanged;

        if (!targetChanged)
            return;

        _target = target;

        if (_isAnimating)
            return;

        _isAnimating = true;
        _lastSetOffset = Offset;
        _lastFrameTime = null;
        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.Handled || _autoScrollTopLevel != null
            || e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.MiddleButtonPressed
            || (Extent.Height <= Viewport.Height && Extent.Width <= Viewport.Width)
            || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        e.Handled = true;
        StartAutoScroll(topLevel, e.GetPosition(this));
        e.Pointer.Capture(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAnimating = false;
        StopAutoScroll();
        base.OnDetachedFromVisualTree(e);
    }

    private void StartAutoScroll(TopLevel topLevel, Point origin)
    {
        _isAnimating = false;
        _autoScrollTopLevel = topLevel;
        _autoScrollOrigin = _autoScrollPointer = origin;
        _isAutoScrollDragged = false;
        _lastAutoScrollFrameTime = null;

        Cursor = new Cursor(Extent.Width > Viewport.Width
            ? Extent.Height > Viewport.Height ? StandardCursorType.SizeAll : StandardCursorType.SizeWestEast
            : StandardCursorType.SizeNorthSouth);

        // On the window, so the pointer is followed and the next click is caught wherever they happen
        topLevel.AddHandler(PointerMovedEvent, OnAutoScrollPointerMoved, RoutingStrategies.Tunnel, true);
        topLevel.AddHandler(PointerPressedEvent, OnAutoScrollPointerPressed, RoutingStrategies.Tunnel, true);
        topLevel.AddHandler(PointerReleasedEvent, OnAutoScrollPointerReleased, RoutingStrategies.Tunnel, true);
        topLevel.AddHandler(PointerWheelChangedEvent, OnAutoScrollWheel, RoutingStrategies.Tunnel, true);
        topLevel.AddHandler(KeyDownEvent, OnAutoScrollKeyDown, RoutingStrategies.Tunnel, true);
        topLevel.RequestAnimationFrame(OnAutoScrollFrame);
    }

    private void StopAutoScroll()
    {
        if (_autoScrollTopLevel is not { } topLevel)
            return;

        _autoScrollTopLevel = null;
        ClearValue(CursorProperty);

        topLevel.RemoveHandler(PointerMovedEvent, OnAutoScrollPointerMoved);
        topLevel.RemoveHandler(PointerPressedEvent, OnAutoScrollPointerPressed);
        topLevel.RemoveHandler(PointerReleasedEvent, OnAutoScrollPointerReleased);
        topLevel.RemoveHandler(PointerWheelChangedEvent, OnAutoScrollWheel);
        topLevel.RemoveHandler(KeyDownEvent, OnAutoScrollKeyDown);
    }

    private void OnAutoScrollPointerMoved(object? sender, PointerEventArgs e)
    {
        _autoScrollPointer = e.GetPosition(this);

        if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed
            && AutoScrollVelocity() != default)
            _isAutoScrollDragged = true;
    }

    // Any click ends the scrolling and does nothing else
    private void OnAutoScrollPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        StopAutoScroll();
    }

    private void OnAutoScrollPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Middle)
            return;

        e.Handled = true;
        if (_isAutoScrollDragged)
            StopAutoScroll();
    }

    private void OnAutoScrollWheel(object? sender, PointerWheelEventArgs e) => StopAutoScroll();

    private void OnAutoScrollKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        StopAutoScroll();
    }

    private void OnAutoScrollFrame(TimeSpan time)
    {
        if (_autoScrollTopLevel is not { } topLevel)
            return;

        var elapsedSeconds = _lastAutoScrollFrameTime is { } lastFrameTime
            ? Math.Max((time - lastFrameTime).TotalSeconds, 0)
            : FirstFrameSeconds;
        _lastAutoScrollFrameTime = time;

        var velocity = AutoScrollVelocity();
        if (velocity != default)
            SetCurrentValue(OffsetProperty, ClampToExtent(Offset + velocity * elapsedSeconds));

        topLevel.RequestAnimationFrame(OnAutoScrollFrame);
    }

    // Pixels per second along each axis that scrolls, from how far the pointer is from where it was pressed
    private Vector AutoScrollVelocity()
    {
        var distance = _autoScrollPointer - _autoScrollOrigin;
        return new Vector(
            Extent.Width > Viewport.Width ? AxisSpeed(distance.X) : 0,
            Extent.Height > Viewport.Height ? AxisSpeed(distance.Y) : 0);

        static double AxisSpeed(double distance) =>
            Math.Sign(distance) * Math.Max(Math.Abs(distance) - AutoScrollDeadZone, 0) * AutoScrollSpeed;
    }

    private void OnAnimationFrame(TimeSpan time)
    {
        if (!_isAnimating)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            _isAnimating = false;
            return;
        }

        var offset = Offset;

        // The scroll bar, the keyboard or code has moved the content meanwhile: the rest of the way is kept,
        // so a list that shifts its offset to hold its rows in place does not cut the scrolling short
        if (offset != _lastSetOffset)
            _target = ClampToExtent(_target + (offset - _lastSetOffset));

        var elapsedSeconds = _lastFrameTime is { } lastFrameTime
            ? Math.Max((time - lastFrameTime).TotalSeconds, 0)
            : FirstFrameSeconds;
        _lastFrameTime = time;

        var remaining = _target - offset;
        var timeConstant = Motion.Get(MotionDuration.Medium).TotalSeconds * TimeConstantShare;

        if (timeConstant <= 0
            || (Math.Abs(remaining.X) < ArrivalDistance && Math.Abs(remaining.Y) < ArrivalDistance))
        {
            SetCurrentValue(OffsetProperty, _target);
            _isAnimating = false;
            return;
        }

        var share = 1 - Math.Exp(-elapsedSeconds / timeConstant);
        SetCurrentValue(OffsetProperty, offset + remaining * share);
        _lastSetOffset = Offset;

        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private static bool IsScrolledInPixels(Control child)
    {
        return child is IPixelScrollable or ItemsPresenter { Panel: IPixelScrollable };
    }

    private Vector ClampToExtent(Vector offset)
    {
        var maxX = Math.Max(Extent.Width - Viewport.Width, 0);
        var maxY = Math.Max(Extent.Height - Viewport.Height, 0);
        return new Vector(Math.Clamp(offset.X, 0, maxX), Math.Clamp(offset.Y, 0, maxY));
    }
}
