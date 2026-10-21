using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Media;

namespace Client.Avalonia.Styles.Animation;

/// <summary>
/// The presenter of every <see cref="ScrollViewer"/>: a turn of the mouse wheel moves the content to its
/// new place over several frames instead of jumping there. Each turn adds to the place the content is
/// heading to, so fast turning covers the same distance as it does without the animation.
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

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAnimating = false;
        base.OnDetachedFromVisualTree(e);
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
