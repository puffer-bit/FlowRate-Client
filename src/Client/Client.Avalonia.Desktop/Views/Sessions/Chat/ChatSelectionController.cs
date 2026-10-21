using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Client.ViewModels.Sessions.Chat;
using Client.Views.Common;

namespace Client.Views.Sessions.Chat;

/// <summary>
/// Text selection across the messages of <see cref="ChatSessionControl"/>, as in Discord: a drag that leaves
/// the text it started in selects the names and texts of every message between its ends.
/// A drag inside one text is left to that <see cref="SelectableTextBlock"/>.
/// </summary>
internal sealed class ChatSelectionController
{
    // The parts of a message in reading order; the edges stand for the space above and below its texts
    private const int BeforeParts = -1;
    private const int NamePart = 0;
    private const int BodyPart = 1;
    private const int AfterParts = 2;

    // A drag this close to the top or bottom edge scrolls the list, faster the closer it gets
    private const double ScrollEdge = 40;
    private const double MaxScrollStep = 24;

    private readonly record struct Position(object Item, int Part, int Offset);

    private readonly ItemsControl _list;

    private Position? _anchor;
    private Position? _focus;
    private bool _isDragging;

    private readonly DispatcherTimer _scrollTimer;
    private Point _pointer;

    public ChatSelectionController(ItemsControl list)
    {
        _list = list;
        _list.Focusable = true;

        // On the way down, so the drag is known before the text block under the pointer takes it
        _list.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        _list.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        _list.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        _list.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        // Rows are recycled while scrolling, so each one gets its part of the selection when it is shown
        _list.ContainerPrepared += (_, _) => Dispatcher.UIThread.Post(Apply, DispatcherPriority.Background);

        _scrollTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        _scrollTimer.Tick += (_, _) => OnScrollTick();
    }

    /// <summary>
    /// True while a selection covers more than one text.
    /// </summary>
    public bool HasSelection => _anchor != null && _focus != null && !_isDragging && GetText().Length > 0;

    public void Clear()
    {
        bool had = _focus != null;
        _anchor = null;
        _focus = null;
        _isDragging = false;
        _scrollTimer.Stop();
        if (had)
            Apply();
    }

    /// <summary>
    /// The selected names and texts, a message per line; a fully selected name carries the time.
    /// </summary>
    public string GetText()
    {
        if (!TryGetRange(out var start, out var end, out int first, out int last))
            return "";

        var text = new StringBuilder();
        for (int i = first; i <= last; i++)
        {
            // A date between the ends is copied as its own line, the way it is shown
            if (_list.Items[i] is ChatDateSeparatorVM date)
            {
                if (i > first && i < last)
                {
                    if (text.Length > 0)
                        text.Append('\n');
                    text.Append(date.DateText);
                }
                continue;
            }

            if (_list.Items[i] is not ChatMessageVM vm)
                continue;

            foreach (var (part, partText) in Parts(vm))
            {
                var (from, to) = PartRange(start, end, first, last, i, part, partText.Length);
                if (from >= to)
                    continue;

                if (text.Length > 0)
                    text.Append('\n');
                text.Append(partText, from, to - from);
                if (part == NamePart && from == 0 && to == partText.Length)
                    text.Append(" — ").Append(vm.Message.SentTime.ToString("HH:mm"));
            }
        }
        return text.ToString();
    }

    private static IEnumerable<(int Part, string Text)> Parts(ChatMessageVM vm)
    {
        if (vm.IsSequenceStart && !string.IsNullOrEmpty(vm.SenderName))
            yield return (NamePart, vm.SenderName);
        if (vm.HasText && vm.Body?.Text is { Length: > 0 } body)
            yield return (BodyPart, body);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed)
            return;

        Clear();

        // Double and triple clicks select a word or a line of one text
        if (e.ClickCount > 1)
            return;

        _anchor = HitTest(e.GetPosition(_list));
        _isDragging = _anchor != null;

        // Without a text under the pointer the keys still have to reach the list for Ctrl+C
        if (_anchor is { Part: BeforeParts or AfterParts })
            _list.Focus();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _anchor is not { } anchor || !e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed)
            return;

        _pointer = e.GetPosition(_list);
        var focus = HitTest(_pointer);
        if (focus == null)
            return;

        bool sameText = focus.Value.Item == anchor.Item && focus.Value.Part == anchor.Part
                                                           && anchor.Part is NamePart or BodyPart;
        if (_focus == null && sameText)
            return;

        if (_focus == null)
        {
            // The text block the drag started in lets the drag go; the list holds the keys from now on
            e.Pointer.Capture(_list);
            _list.Focus();
        }

        _focus = focus;
        e.Handled = true;
        Apply();

        if (ScrollStep() != 0)
            _scrollTimer.Start();
        else
            _scrollTimer.Stop();
    }

    // Pixels to scroll per tick for the pointer position: negative up, positive down
    private double ScrollStep()
    {
        double height = _list.Bounds.Height;
        double depth = _pointer.Y < ScrollEdge ? _pointer.Y - ScrollEdge
            : _pointer.Y > height - ScrollEdge ? _pointer.Y - (height - ScrollEdge)
            : 0;
        return Math.Clamp(depth / ScrollEdge, -1, 1) * MaxScrollStep;
    }

    private void OnScrollTick()
    {
        double step = ScrollStep();
        if (!_isDragging || _focus == null || step == 0
            || _list.FindDescendantOfType<ScrollViewer>() is not { } scroller)
        {
            _scrollTimer.Stop();
            return;
        }

        var offset = scroller.Offset;
        double y = Math.Clamp(offset.Y + step, 0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height));
        if (y == offset.Y)
            return;

        scroller.Offset = offset.WithY(y);

        // The rows under the pointer change after the layout pass, so the end of the selection follows them then
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isDragging || HitTest(_pointer) is not { } focus)
                return;
            _focus = focus;
            Apply();
        }, DispatcherPriority.Loaded);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        _scrollTimer.Stop();
        if (_focus != null)
        {
            e.Pointer.Capture(null);
            e.Handled = true;
            Apply();
        }
        else
        {
            _anchor = null;
        }
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || !HasSelection)
            return;

        e.Handled = true;
        await ClipboardAccess.SetTextAsync(_list, GetText());
    }

    private bool TryGetRange(out Position start, out Position end, out int first, out int last)
    {
        start = end = default;
        first = last = -1;
        if (_anchor is not { } anchor || _focus is not { } focus)
            return false;

        int anchorIndex = _list.Items.IndexOf(anchor.Item);
        int focusIndex = _list.Items.IndexOf(focus.Item);
        if (anchorIndex < 0 || focusIndex < 0)
            return false;

        bool forward = (anchorIndex, anchor.Part, anchor.Offset).CompareTo((focusIndex, focus.Part, focus.Offset)) <= 0;
        (start, first) = forward ? (anchor, anchorIndex) : (focus, focusIndex);
        (end, last) = forward ? (focus, focusIndex) : (anchor, anchorIndex);
        return true;
    }

    // The selected characters of one part of the message at index
    private static (int From, int To) PartRange(Position start, Position end, int first, int last,
        int index, int part, int length)
    {
        int from = (index, part).CompareTo((first, start.Part)) switch
        {
            < 0 => length,
            0 => Math.Clamp(start.Offset, 0, length),
            > 0 => 0
        };
        int to = (index, part).CompareTo((last, end.Part)) switch
        {
            < 0 => length,
            0 => Math.Clamp(end.Offset, 0, length),
            > 0 => 0
        };
        return (from, to);
    }

    private void Apply()
    {
        bool hasRange = TryGetRange(out var start, out var end, out int first, out int last);

        foreach (var container in _list.GetRealizedContainers())
        {
            if (FindMessage(container) is not { } message)
                continue;

            int index = _list.IndexFromContainer(container);
            foreach (var (part, block) in Blocks(message))
            {
                int length = TextOf(block)?.Length ?? 0;
                var (from, to) = hasRange ? PartRange(start, end, first, last, index, part, length) : (0, 0);

                // The block the user is dragging in keeps its own selection until the drag leaves it
                if (!hasRange && block.IsFocused)
                    continue;
                if (from >= to)
                    from = to = 0;
                block.SelectionStart = from;
                block.SelectionEnd = to;
            }
        }
    }

    private static ChatMessageControl? FindMessage(Control container) =>
        container as ChatMessageControl
        ?? (container as ContentPresenter)?.Child as ChatMessageControl;

    private static IEnumerable<(int Part, SelectableTextBlock Block)> Blocks(ChatMessageControl message)
    {
        yield return (NamePart, message.NameTextBlock);
        yield return (BodyPart, message.MessageTextBlock);
    }

    private static string? TextOf(SelectableTextBlock block) =>
        block.Name == "NameTextBlock" ? block.Text : ChatMessageText.GetText(block);

    // The place in the text nearest to a point of the list
    private Position? HitTest(Point point)
    {
        Control? nearest = null;
        double nearestDistance = double.MaxValue;

        foreach (var container in _list.GetRealizedContainers())
        {
            if (FindMessage(container) == null || !container.IsVisible)
                continue;
            if (container.TranslatePoint(default, _list) is not { } topLeft)
                continue;

            double top = topLeft.Y, bottom = top + container.Bounds.Height;
            double distance = point.Y < top ? top - point.Y : point.Y > bottom ? point.Y - bottom : 0;
            if (distance < nearestDistance)
            {
                nearest = container;
                nearestDistance = distance;
            }
        }

        if (nearest == null || FindMessage(nearest) is not { } message || message.DataContext is not ChatMessageVM vm)
            return null;

        var hit = new Position(vm, BeforeParts, 0);
        foreach (var (part, block) in Blocks(message))
        {
            if (!block.IsEffectivelyVisible || string.IsNullOrEmpty(TextOf(block)))
                continue;
            if (_list.TranslatePoint(point, block) is not { } local)
                continue;

            if (local.Y < 0)
                return hit;

            var padding = block.Padding;
            var inText = new Point(local.X - padding.Left, Math.Min(local.Y, block.Bounds.Height) - padding.Top);
            var textHit = block.TextLayout.HitTestPoint(inText);
            hit = new Position(vm, part, textHit.TextPosition);

            if (local.Y <= block.Bounds.Height)
                return hit;
        }

        // Below every text of the message: the end of its last text, or past it when it has none
        return hit.Part == BeforeParts ? new Position(vm, AfterParts, 0) : hit;
    }
}
