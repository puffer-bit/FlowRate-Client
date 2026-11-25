using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Client.Avalonia.Controls.Images;
using Client.Avalonia.Styles.Animation;

namespace Client.Mobile.Views.Common;

/// <summary>
/// A finger dragged along one axis over a control, for gestures such as swiping a screen away. The
/// drag starts once the finger has gone far enough and clearly along the axis; until then whatever is
/// under the finger has it, so taps, long presses and scrolling across the axis are left alone. From
/// then on the drag takes the pointer from them.
/// </summary>
/// <remarks>
/// A press on what moves along the axis itself, such as a slider, a text field, a list that scrolls
/// sideways or a zoomed picture, never starts a drag; nor does a press of two fingers.
/// </remarks>
public sealed class DragGesture
{
    /// <summary>How far the finger goes before the drag starts or is given up.</summary>
    private const double LockDistance = 10;
    // Along the axis by at least this much more than across it
    private const double AxisRatio = 1.2;
    // The speed of a fling is taken from the last this many milliseconds of the drag
    private const ulong VelocityWindow = 100;

    /// <summary>A fling at least this fast, in pixels a second, finishes a gesture however short it is.</summary>
    public const double FlingVelocity = 800;

    private readonly InputElement _host;
    private readonly Orientation _axis;

    private IPointer? _pointer;
    private Visual? _source;
    private Point _start;
    private double _lockOffset;
    private double _offset;
    private bool _isDragging;
    // The press went another way, or another finger joined it: nothing more happens until it is lifted
    private bool _isRejected;
    private readonly List<(ulong Time, double Offset)> _samples = new();

    public DragGesture(InputElement host, Orientation axis)
    {
        _host = host;
        _axis = axis;

        // Tunneled, so the drag is decided before the controls under the finger act on the move
        host.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, handledEventsToo: true);
    }

    /// <summary>
    /// Asked once the finger has gone far enough along the axis: whether this drag is for the gesture.
    /// Given what was pressed and the offset so far, whose sign is the direction.
    /// </summary>
    public Func<Visual?, double, bool>? CanStart { get; init; }

    /// <summary>The finger moved; the offset is along the axis, from where the drag started.</summary>
    public Action<double>? Moved { get; init; }

    /// <summary>The finger was lifted, with the last offset and the speed of the fling in pixels a second.</summary>
    public Action<double, double>? Ended { get; init; }

    public bool IsDragging => _isDragging;

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        // A second finger: a pinch, not a drag
        if (!e.Pointer.IsPrimary)
        {
            if (_pointer != null && !_isDragging)
                _isRejected = true;
            return;
        }

        if (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed)
            return;

        // A primary press means no other finger is down: what is still tracked was lifted where the
        // release never came here, e.g. after the list took the finger for scrolling
        if (_pointer != null && _isDragging)
        {
            Reset();
            Ended?.Invoke(_offset, 0);
        }

        _pointer = e.Pointer;
        _source = e.Source as Visual;
        HoldOffScrolling(_source);
        _start = e.GetPosition(_host);
        _isDragging = false;
        _isRejected = false;
        _samples.Clear();
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer) || _isRejected)
            return;

        var delta = e.GetPosition(_host) - _start;
        var along = _axis == Orientation.Horizontal ? delta.X : delta.Y;
        var across = _axis == Orientation.Horizontal ? delta.Y : delta.X;

        if (!_isDragging)
        {
            if (Math.Max(Math.Abs(along), Math.Abs(across)) < LockDistance)
                return;

            if (Math.Abs(along) < Math.Abs(across) * AxisRatio || MovesItself(_source)
                || CanStart?.Invoke(_source, along) != true)
            {
                _isRejected = true;
                return;
            }

            _isDragging = true;
            // The content starts from where it is, not with a jump to the finger
            _lockOffset = Math.Sign(along) * LockDistance;
            e.Pointer.Capture(_host);
        }

        _offset = along - _lockOffset;
        _samples.Add((e.Timestamp, _offset));
        if (_samples.Count > 1 && e.Timestamp - _samples[0].Time > VelocityWindow)
            _samples.RemoveAt(0);

        Moved?.Invoke(_offset);
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer))
            return;

        var wasDragging = _isDragging;
        var velocity = Velocity();
        Reset();
        if (!wasDragging)
            return;

        e.Handled = true;
        if (ReferenceEquals(e.Pointer.Captured, _host))
            e.Pointer.Capture(null);
        Ended?.Invoke(_offset, velocity);
    }

    // Taken away by the system or another control, such as a list that started scrolling: the finger is
    // no longer ours, and a gesture under way ends where it is, without a fling
    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer))
            return;

        // The drag itself letting go on release is not a loss
        if (_isDragging && ReferenceEquals(e.Pointer.Captured, _host))
            return;

        var wasDragging = _isDragging;
        Reset();
        if (wasDragging)
            Ended?.Invoke(_offset, 0);
    }

    private void Reset()
    {
        _pointer = null;
        _source = null;
        _isDragging = false;
        _isRejected = false;
    }

    private double Velocity()
    {
        if (_samples.Count < 2)
            return 0;

        var (firstTime, firstOffset) = _samples[0];
        var (lastTime, lastOffset) = _samples[^1];
        return lastTime > firstTime ? (lastOffset - firstOffset) * 1000 / (lastTime - firstTime) : 0;
    }

    // A list under the finger starts scrolling after a few pixels across the axis and from then on takes
    // every move, before the drag could tell where the finger is going: it waits until the drag has decided.
    // Going across, the drag gives up at the lock distance and the list scrolls right after
    private void HoldOffScrolling(Visual? source)
    {
        for (var visual = source; visual != null && !ReferenceEquals(visual, _host); visual = visual.GetVisualParent())
        {
            if (visual is not InputElement element)
                continue;

            foreach (var recognizer in element.GestureRecognizers)
            {
                if (recognizer is ScrollGestureRecognizer scroll && scroll.ScrollStartDistance <= LockDistance)
                    scroll.ScrollStartDistance = (int)LockDistance + 1;
            }
        }
    }

    // What has a drag of its own along the axis keeps the finger
    private bool MovesItself(Visual? source)
    {
        for (var visual = source; visual != null && !ReferenceEquals(visual, _host); visual = visual.GetVisualParent())
        {
            switch (visual)
            {
                case Slider or TextBox or ScrollBar:
                case ZoomableImage { IsZoomed: true }:
                case ZoomDecorator { IsZoomed: true }:
                case ScrollViewer scroll when (_axis == Orientation.Horizontal
                    ? scroll.Extent.Width > scroll.Viewport.Width
                    : scroll.Extent.Height > scroll.Viewport.Height):
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Runs a step for every frame for the given time, with the progress from 0 to 1 eased as the
    /// motion of the application is. Moved frame by frame rather than by an animation: an animation lets
    /// go of its value when it ends, and what it moved jumps back for a frame.
    /// </summary>
    public static Task Animate(Visual visual, TimeSpan duration, Action<double> step)
    {
        if (duration <= TimeSpan.Zero || TopLevel.GetTopLevel(visual) is not { } topLevel)
        {
            step(1);
            return Task.CompletedTask;
        }

        var ease = Motion.Ease(MotionEase.Standard);
        var finished = new TaskCompletionSource();
        TimeSpan? start = null;

        void Frame(TimeSpan time)
        {
            start ??= time;
            var progress = Math.Clamp((time - start.Value) / duration, 0, 1);
            step(ease.Ease(progress));
            if (progress < 1)
                topLevel.RequestAnimationFrame(Frame);
            else
                finished.TrySetResult();
        }

        topLevel.RequestAnimationFrame(Frame);
        return finished.Task;
    }
}
