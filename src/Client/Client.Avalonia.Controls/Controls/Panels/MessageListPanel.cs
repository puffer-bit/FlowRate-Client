using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Client.Avalonia.Styles.Animation;

namespace Client.Avalonia.Controls.Panels;

/// <summary>
/// The items panel of a message list: a virtualizing vertical stack that scrolls itself.
/// It remembers the height of every row it has measured, so a row always lies at the sum of the heights
/// above it and the first row always starts at the very top. The scroll offset is not kept between layout
/// passes: it is worked out from a visible row (the anchor) and the place that row had on screen, so rows
/// added, removed or resized above the anchor change the offset and never move what the user is looking at.
/// While the list is scrolled to its end it stays there whatever happens to the rows.
/// </summary>
public class MessageListPanel : VirtualizingPanel, ILogicalScrollable, IPixelScrollable
{
    /// <summary>
    /// Raised after a layout pass that changed the offset, the extent or the viewport.
    /// </summary>
    public static readonly RoutedEvent<RoutedEventArgs> ScrollStateChangedEvent =
        RoutedEvent.Register<MessageListPanel, RoutedEventArgs>(nameof(ScrollStateChanged), RoutingStrategies.Bubble);

    private static readonly AttachedProperty<object?> RecycleKeyProperty =
        AvaloniaProperty.RegisterAttached<MessageListPanel, Control, object?>("RecycleKey");

    private static readonly object ItemIsItsOwnContainer = new();

    // The height taken for a row until the first rows are measured
    private const double DefaultRowHeight = 48;
    // Rows are created up to this share of the viewport beyond its edges and dropped
    // past the larger one, so a small scroll back and forth does not rebuild a row
    private const double RealizeReach = 0.5;
    private const double KeepReach = 1.0;
    private const double WheelStep = 50;
    private const double EndTolerance = 0.5;
    private const double OffsetTolerance = 0.01;
    // Measuring rows replaces estimated heights, which can move the limits of the offset;
    // the layout is repeated until the offset fits them
    private const int MaxLayoutPasses = 8;

    private enum Alignment
    {
        // The anchor keeps the place it had on screen
        Keep,
        Center,
        Bottom
    }

    // One height per item: measured, or an estimate for a row not seen yet
    private readonly List<double> _heights = new();

    // The rows that have a container; _rows[i] belongs to the item _firstRow + i.
    // An empty slot is an item inserted between two rows, filled in by the next layout pass
    private readonly List<Control?> _rows = new();
    private int _firstRow;
    private double _firstRowTop;
    private Dictionary<object, Stack<Control>>? _recyclePool;

    private int _anchorIndex;
    private double _anchorTop;
    private Alignment _alignment;
    private bool _stickToEnd = true;

    private double _offset;
    private double _extent;
    private double _viewport;
    private double _width;

    private bool _isScrollStateDirty;
    private bool _isScrollStateEventPending;
    private bool _isRaisingScrollInvalidated;
    private EventHandler? _scrollInvalidated;

    public event EventHandler<RoutedEventArgs>? ScrollStateChanged
    {
        add => AddHandler(ScrollStateChangedEvent, value);
        remove => RemoveHandler(ScrollStateChangedEvent, value);
    }

    public double ViewportHeight => _viewport;
    public double DistanceToStart => _offset;
    public double DistanceToEnd => Math.Max(0, _extent - _viewport - _offset);

    /// <summary>
    /// Shows the last row and keeps the list at its end until the user scrolls away.
    /// </summary>
    public void ScrollToEnd()
    {
        _stickToEnd = true;
        AnnounceScrollState();
        InvalidateMeasure();
    }

    /// <summary>
    /// Puts the row at <paramref name="index"/> in the middle of the viewport.
    /// </summary>
    public void ScrollToCenter(int index)
    {
        if (index < 0 || index >= _heights.Count)
            return;

        _anchorIndex = index;
        _alignment = Alignment.Center;
        _stickToEnd = false;
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = Items.Count;
        if (_heights.Count != count)
            ResetRows(count);

        double viewport = availableSize.Height;
        bool isViewportFinite = !double.IsInfinity(viewport);

        if (count == 0)
        {
            RecycleAllRows();
            SetScrollState(0, 0, isViewportFinite ? viewport : 0, availableSize.Width);
            return new Size(FiniteOrZero(availableSize.Width), isViewportFinite ? viewport : 0);
        }

        int anchor = _stickToEnd ? count - 1 : Math.Clamp(_anchorIndex, 0, count - 1);
        var alignment = _stickToEnd ? Alignment.Bottom : _alignment;
        double anchorTop = _anchorTop;

        // Without a height limit every row is laid out and nothing scrolls
        if (!isViewportFinite)
        {
            anchor = 0;
            anchorTop = 0;
            alignment = Alignment.Keep;
        }

        // After a far jump the rows that exist are of no use to the new place
        if (_rows.Count > 0 && (anchor < _firstRow - 1 || anchor > _firstRow + _rows.Count))
            RecycleAllRows();

        var constraint = new Size(availableSize.Width, double.PositiveInfinity);
        double offset = 0;
        double extent = 0;
        int first = anchor;
        int last = anchor;

        for (int pass = 0; pass < MaxLayoutPasses; pass++)
        {
            double anchorHeight = MeasureRow(anchor, constraint);

            if (alignment == Alignment.Bottom)
                anchorTop = viewport - anchorHeight;
            else if (alignment == Alignment.Center)
                anchorTop = Math.Max(0, (viewport - anchorHeight) / 2);

            first = last = anchor;
            double top = anchorTop;
            double bottom = anchorTop + anchorHeight;

            while (first > 0 && top > -Reach(first - 1, viewport))
                top -= MeasureRow(--first, constraint);

            while (last < count - 1 && bottom < viewport + Reach(last + 1, viewport))
                bottom += MeasureRow(++last, constraint);

            double anchorY = SumHeights(0, anchor);
            extent = anchorY + SumHeights(anchor, count);
            offset = anchorY - anchorTop;

            double clamped = Math.Clamp(offset, 0, Math.Max(0, extent - viewport));
            if (Math.Abs(clamped - offset) < OffsetTolerance)
                break;

            // The anchor cannot stay where it was: it moves with the offset, and the rows are filled in again
            offset = clamped;
            anchorTop = anchorY - clamped;
            alignment = Alignment.Keep;
        }

        RecycleRowsOutside(first, last);
        _firstRowTop = SumHeights(0, first);

        if (!isViewportFinite)
            viewport = extent;

        _alignment = Alignment.Keep;
        _stickToEnd = offset >= extent - viewport - EndTolerance;
        SetScrollState(offset, extent, viewport, availableSize.Width);
        PickAnchor();

        double width = availableSize.Width;
        if (double.IsInfinity(width))
        {
            width = 0;
            foreach (var row in _rows)
                width = Math.Max(width, row!.DesiredSize.Width);
        }

        return new Size(width, viewport);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = _firstRowTop - _offset;
        for (int i = 0; i < _rows.Count; i++)
        {
            double height = _heights[_firstRow + i];
            _rows[i]?.Arrange(new Rect(0, y, finalSize.Width, height));
            y += height;
        }

        if (_isScrollStateDirty)
        {
            _isScrollStateDirty = false;
            RaiseScrollInvalidated(EventArgs.Empty);

            // Handlers change view models and ask for more rows, which is no work for a layout pass
            if (!_isScrollStateEventPending)
            {
                _isScrollStateEventPending = true;
                Dispatcher.UIThread.Post(() =>
                {
                    _isScrollStateEventPending = false;
                    RaiseEvent(new RoutedEventArgs(ScrollStateChangedEvent));
                });
            }
        }

        return finalSize;
    }

    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                InsertRows(e.NewStartingIndex, e.NewItems!.Count);
                break;
            case NotifyCollectionChangedAction.Remove:
                RemoveRows(e.OldStartingIndex, e.OldItems!.Count);
                break;
            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Move:
                RemoveRows(e.OldStartingIndex, e.OldItems!.Count);
                InsertRows(e.NewStartingIndex, e.NewItems!.Count);
                break;
            default:
                ResetRows(items.Count);
                break;
        }

        AnnounceScrollState();
        InvalidateMeasure();
    }

    protected override void OnItemsControlChanged(ItemsControl? oldValue)
    {
        base.OnItemsControlChanged(oldValue);

        // The children are dropped together with the old items control
        _rows.Clear();
        _recyclePool = null;
        ResetRows(Items.Count);
    }

    protected override Control? ScrollIntoView(int index)
    {
        if (index < 0 || index >= _heights.Count)
            return null;

        double top = SumHeights(0, index) - _offset;
        double bottom = top + _heights[index];

        if (ContainerFromIndex(index) == null || top < 0 || bottom > _viewport)
        {
            _anchorIndex = index;
            _anchorTop = 0;
            // The nearest edge, and the top of a row taller than the viewport
            _alignment = top >= 0 && _heights[index] <= _viewport ? Alignment.Bottom : Alignment.Keep;
            _stickToEnd = false;
            InvalidateMeasure();
            UpdateLayout();
        }

        return ContainerFromIndex(index);
    }

    protected override Control? ContainerFromIndex(int index)
    {
        int slot = index - _firstRow;
        return slot >= 0 && slot < _rows.Count ? _rows[slot] : null;
    }

    protected override int IndexFromContainer(Control container)
    {
        int slot = _rows.IndexOf(container);
        return slot == -1 ? -1 : _firstRow + slot;
    }

    protected override IEnumerable<Control> GetRealizedContainers()
    {
        foreach (var row in _rows)
        {
            if (row != null)
                yield return row;
        }
    }

    // Messages are not items to walk through with the arrow keys
    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        return null;
    }

    private double MeasureRow(int index, Size constraint)
    {
        if (_rows.Count == 0)
        {
            _firstRow = index;
            _rows.Add(null);
        }
        else if (index == _firstRow - 1)
        {
            _firstRow = index;
            _rows.Insert(0, null);
        }
        else if (index == _firstRow + _rows.Count)
        {
            _rows.Add(null);
        }

        int slot = index - _firstRow;
        var row = _rows[slot] ??= RealizeRow(index);

        row.Measure(constraint);
        _heights[index] = row.DesiredSize.Height;
        return _heights[index];
    }

    private double Reach(int index, double viewport)
    {
        return viewport * (ContainerFromIndex(index) != null ? KeepReach : RealizeReach);
    }

    private double SumHeights(int from, int to)
    {
        double sum = 0;
        for (int i = from; i < to; i++)
            sum += _heights[i];

        return sum;
    }

    // A new row is taken to be as tall as the rows on screen are on average
    private double EstimateHeight()
    {
        double sum = 0;
        int count = 0;
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i] == null)
                continue;

            sum += _heights[_firstRow + i];
            count++;
        }

        return count > 0 ? sum / count : DefaultRowHeight;
    }

    /// <summary>
    /// Remembers the lowest row in the viewport and where it stands. The lowest one, because rows that
    /// join the list above it belong to the history and must push the offset, not the row.
    /// </summary>
    private void PickAnchor()
    {
        int count = _heights.Count;
        double top = 0;
        int index = 0;
        while (index < count - 1 && top + _heights[index] < _offset + _viewport)
            top += _heights[index++];

        _anchorIndex = index;
        _anchorTop = top - _offset;
    }

    private void InsertRows(int index, int count)
    {
        bool wasEmpty = _heights.Count == 0;
        double estimate = EstimateHeight();
        for (int i = 0; i < count; i++)
            _heights.Insert(index, estimate);

        if (!wasEmpty && index <= _anchorIndex)
            _anchorIndex += count;

        if (_rows.Count == 0 || index >= _firstRow + _rows.Count)
            return;

        if (index <= _firstRow)
        {
            ShiftRowIndexes(0, count);
            _firstRow += count;
            return;
        }

        int slot = index - _firstRow;
        ShiftRowIndexes(slot, count);
        for (int i = 0; i < count; i++)
            _rows.Insert(slot, null);
    }

    private void RemoveRows(int index, int count)
    {
        _heights.RemoveRange(index, count);

        // A removed anchor hands its place to the row that followed it
        if (_anchorIndex >= index + count)
            _anchorIndex -= count;
        else if (_anchorIndex > index)
            _anchorIndex = index;

        if (_rows.Count == 0)
            return;

        int end = index + count;
        int windowEnd = _firstRow + _rows.Count;
        int removedFrom = Math.Max(index, _firstRow);
        int removedTo = Math.Min(end, windowEnd);

        if (removedTo > removedFrom)
        {
            for (int i = removedFrom; i < removedTo; i++)
            {
                if (_rows[i - _firstRow] is { } row)
                    RecycleRow(row);
            }

            _rows.RemoveRange(removedFrom - _firstRow, removedTo - removedFrom);
        }

        int removedBefore = Math.Max(0, Math.Min(end, _firstRow) - index);
        int firstShifted = Math.Max(0, removedFrom - _firstRow);

        // _firstRow still counts the removed rows here, so the old indexes come out right
        if (end < windowEnd)
        {
            for (int slot = firstShifted; slot < _rows.Count; slot++)
            {
                if (_rows[slot] is not { } row)
                    continue;

                int newIndex = _firstRow - removedBefore + slot;
                ItemContainerGenerator!.ItemContainerIndexChanged(row, newIndex + count, newIndex);
            }
        }

        _firstRow -= removedBefore;
    }

    private void ShiftRowIndexes(int fromSlot, int delta)
    {
        for (int slot = fromSlot; slot < _rows.Count; slot++)
        {
            if (_rows[slot] is { } row)
                ItemContainerGenerator!.ItemContainerIndexChanged(row, _firstRow + slot, _firstRow + slot + delta);
        }
    }

    /// <summary>
    /// Starts over with <paramref name="count"/> unmeasured rows, shown from the end.
    /// </summary>
    private void ResetRows(int count)
    {
        double estimate = EstimateHeight();
        RecycleAllRows();

        _heights.Clear();
        for (int i = 0; i < count; i++)
            _heights.Add(estimate);

        _anchorIndex = 0;
        _anchorTop = 0;
        _alignment = Alignment.Keep;
        _stickToEnd = true;
    }

    private void RecycleRowsOutside(int first, int last)
    {
        for (int slot = _rows.Count - 1; slot > last - _firstRow; slot--)
        {
            if (_rows[slot] is { } row)
                RecycleRow(row);

            _rows.RemoveAt(slot);
        }

        int before = first - _firstRow;
        if (before <= 0)
            return;

        for (int slot = 0; slot < before; slot++)
        {
            if (_rows[slot] is { } row)
                RecycleRow(row);
        }

        _rows.RemoveRange(0, before);
        _firstRow = first;
    }

    private void RecycleAllRows()
    {
        foreach (var row in _rows)
        {
            if (row != null)
                RecycleRow(row);
        }

        _rows.Clear();
    }

    private Control RealizeRow(int index)
    {
        var item = Items[index];
        var generator = ItemContainerGenerator!;

        if (!generator.NeedsContainer(item, index, out var recycleKey))
        {
            var control = (Control)item!;
            if (!control.IsSet(RecycleKeyProperty))
            {
                generator.PrepareItemContainer(control, control, index);
                AddInternalChild(control);
                control.SetValue(RecycleKeyProperty, ItemIsItsOwnContainer);
                generator.ItemContainerPrepared(control, item, index);
            }
            else
            {
                AddInternalChild(control);
            }

            return control;
        }

        Control? row = null;
        if (recycleKey != null && _recyclePool?.TryGetValue(recycleKey, out var pool) == true)
            pool.TryPop(out row);

        // A hidden presenter is still a child and still holds its view: the new item goes straight
        // into it, so the data template reuses that view instead of building a new one
        if (row is ContentPresenter { IsVisible: false } presenter)
        {
            presenter.Content = item;
            generator.PrepareItemContainer(presenter, item, index);
            presenter.IsVisible = true;
            generator.ItemContainerPrepared(presenter, item, index);
            return presenter;
        }

        if (row == null)
        {
            row = generator.CreateContainer(item, index, recycleKey);
            row.SetValue(RecycleKeyProperty, recycleKey);
        }

        generator.PrepareItemContainer(row, item, index);
        AddInternalChild(row);
        generator.ItemContainerPrepared(row, item, index);
        return row;
    }

    private void RecycleRow(Control row)
    {
        var recycleKey = row.GetValue(RecycleKeyProperty);

        if (recycleKey == ItemIsItsOwnContainer)
        {
            RemoveInternalChild(row);
            return;
        }

        if (recycleKey == null)
        {
            ItemContainerGenerator!.ClearItemContainer(row);
            RemoveInternalChild(row);
            return;
        }

        // Clearing a presenter drops its view, and leaving the tree drops its styles; both would be
        // built again for the next row. It is only hidden, and keeps its view until it shows another item
        if (row is ContentPresenter)
            row.IsVisible = false;
        else
        {
            ItemContainerGenerator!.ClearItemContainer(row);
            RemoveInternalChild(row);
        }

        _recyclePool ??= new Dictionary<object, Stack<Control>>();
        if (!_recyclePool.TryGetValue(recycleKey, out var pool))
        {
            pool = new Stack<Control>();
            _recyclePool.Add(recycleKey, pool);
        }

        pool.Push(row);
    }



    bool ILogicalScrollable.CanHorizontallyScroll { get; set; }
    bool ILogicalScrollable.CanVerticallyScroll { get; set; }
    bool IScrollable.CanHorizontallyScroll => false;
    bool IScrollable.CanVerticallyScroll => true;
    bool ILogicalScrollable.IsLogicalScrollEnabled => true;
    Size ILogicalScrollable.ScrollSize => new(WheelStep, WheelStep);
    Size ILogicalScrollable.PageScrollSize => new(_width, _viewport);
    Size IScrollable.Extent => new(_width, _extent);
    Size IScrollable.Viewport => new(_width, _viewport);

    Vector IScrollable.Offset
    {
        get => new(0, _offset);
        set
        {
            // While the scroll viewer takes over the new extent and viewport it clamps its offset to a mix
            // of old and new values and sends that back; the offset it ends up with is the one of this panel
            if (_isRaisingScrollInvalidated)
                return;

            ScrollTo(value.Y);
        }
    }

    event EventHandler? ILogicalScrollable.ScrollInvalidated
    {
        add => _scrollInvalidated += value;
        remove => _scrollInvalidated -= value;
    }

    bool ILogicalScrollable.BringIntoView(Control target, Rect targetRect)
    {
        if (target.TranslatePoint(targetRect.TopLeft, this) is not { } position)
            return false;

        double top = position.Y;
        double bottom = top + targetRect.Height;

        // Something that covers the viewport is in view already
        if (top < 0 && bottom <= _viewport)
            return ScrollTo(_offset + top);

        if (top >= 0 && bottom > _viewport)
            return ScrollTo(_offset + Math.Min(top, bottom - _viewport));

        return false;
    }

    Control? ILogicalScrollable.GetControlInDirection(NavigationDirection direction, Control? from)
    {
        return null;
    }

    public void RaiseScrollInvalidated(EventArgs e)
    {
        _isRaisingScrollInvalidated = true;
        try
        {
            _scrollInvalidated?.Invoke(this, e);
        }
        finally
        {
            _isRaisingScrollInvalidated = false;
        }
    }

    /// <summary>
    /// Moves the viewport to <paramref name="offset"/> of the rows as they are laid out now.
    /// </summary>
    private bool ScrollTo(double offset)
    {
        double max = Math.Max(0, _extent - _viewport);
        offset = Math.Clamp(offset, 0, max);
        if (Math.Abs(offset - _offset) < OffsetTolerance)
            return false;

        _offset = offset;
        _alignment = Alignment.Keep;
        _stickToEnd = offset >= max - EndTolerance;
        _isScrollStateDirty = true;
        PickAnchor();
        InvalidateMeasure();
        return true;
    }

    /// <summary>
    /// Tells the scroll viewer where the rows are before they are laid out. An offset it sends in the
    /// meantime, like the next frame of a wheel animation, is then counted from the rows as they are now
    /// and not from the ones that were there before the change.
    /// </summary>
    private void AnnounceScrollState()
    {
        int count = _heights.Count;
        double extent = SumHeights(0, count);
        double max = Math.Max(0, extent - _viewport);
        double offset = _stickToEnd || count == 0
            ? max
            : Math.Clamp(SumHeights(0, Math.Min(_anchorIndex, count - 1)) - _anchorTop, 0, max);

        SetScrollState(offset, extent, _viewport, _width);
        RaiseScrollInvalidated(EventArgs.Empty);
    }

    private void SetScrollState(double offset, double extent, double viewport, double width)
    {
        width = FiniteOrZero(width);

        if (Math.Abs(offset - _offset) >= OffsetTolerance
            || Math.Abs(extent - _extent) >= OffsetTolerance
            || Math.Abs(viewport - _viewport) >= OffsetTolerance
            || Math.Abs(width - _width) >= OffsetTolerance)
        {
            _isScrollStateDirty = true;
        }

        _offset = offset;
        _extent = extent;
        _viewport = viewport;
        _width = width;
    }

    private static double FiniteOrZero(double value)
    {
        return double.IsInfinity(value) ? 0 : value;
    }

}
