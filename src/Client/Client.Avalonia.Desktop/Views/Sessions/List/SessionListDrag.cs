using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Client.ViewModels.Sessions.List;

namespace Client.Views.Sessions.List;

/// <summary>
/// Dragging sessions and categories of the session list to another place. A session goes between
/// sessions, or onto a category header to go first into that category; a category goes between
/// categories. The row stays in place as an empty slot while a copy of its name follows the pointer,
/// and a line shows where it will land.
/// </summary>
internal sealed class SessionListDrag
{
    // How far the pointer moves before a press becomes a drag, so a click stays a click
    private const double StartDistance = 6;

    // Near the edges of the list it scrolls on its own, faster the closer the pointer is
    private const double ScrollZone = 32;
    private const double ScrollSpeed = 14;

    private readonly ListBox _list;
    private readonly Canvas _layer;
    private readonly Border _ghost;
    private readonly TextBlock _ghostText;
    private readonly Border _indicator;
    private readonly Func<SessionsListVM?> _viewModel;
    private readonly DispatcherTimer _scrollTimer;

    private ISessionListRow? _pressedRow;
    private Point _pressPoint;
    private IPointer? _pointer;
    private ISessionListRow? _draggedRow;
    private Point _lastPoint;
    private Target? _target;
    private SessionCategoryVM? _highlighted;

    private sealed record Target(int? CategoryId, ISessionListRow? Before, double? LineY);

    public SessionListDrag(ListBox list, Canvas layer, Func<SessionsListVM?> viewModel)
    {
        _list = list;
        _layer = layer;
        _viewModel = viewModel;

        _ghostText = new TextBlock { MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis };
        _ghost = new Border { Child = _ghostText, IsVisible = false };
        _ghost.Classes.Add("sessionDragGhost");
        _indicator = new Border { IsVisible = false };
        _indicator.Classes.Add("sessionDropIndicator");
        _layer.Children.Add(_indicator);
        _layer.Children.Add(_ghost);

        _scrollTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        _scrollTimer.Tick += (_, _) => AutoScroll();

        _list.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, true);
        _list.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, true);
        _list.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, true);
        _list.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => Cancel(), RoutingStrategies.Bubble, true);
        _list.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, true);
    }

    public bool IsDragging => _draggedRow != null;

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        Cancel();

        // Arranging the list is for those whose roles allow it
        if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed
            || _viewModel() is not { IsStale: false, Access.CanManageSessions: true })
            return;

        // Only the header of a row is a handle: the peers below a session and the controls in a
        // header (the name being typed, the add button) keep their own presses
        if (e.Source is not Visual source
            || source.FindAncestorOfType<TextBox>(true) != null
            || source.FindAncestorOfType<Button>(true) != null
            || FindHeader(source) == null
            || RowOf(source) is not { } row
            || row is SessionCategoryVM { IsEditing: true })
            return;

        _pressedRow = row;
        _pressPoint = e.GetPosition(_list);
        _pointer = e.Pointer;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedRow == null || e.Pointer != _pointer)
            return;

        _lastPoint = e.GetPosition(_list);

        if (_draggedRow == null)
        {
            var moved = _lastPoint - _pressPoint;
            if (Math.Sqrt(moved.X * moved.X + moved.Y * moved.Y) < StartDistance)
                return;

            Begin(e.Pointer);
        }

        Update();
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggedRow == null)
        {
            _pressedRow = null;
            return;
        }

        var row = _draggedRow;
        var target = _target;
        _pressedRow = null;
        End();
        e.Handled = true;

        if (target == null || _viewModel() is not { } viewModel)
            return;

        switch (row)
        {
            case ISessionVM session:
                _ = viewModel.MoveSession(session, target.CategoryId, target.Before as ISessionVM);
                break;
            case SessionCategoryVM category:
                _ = viewModel.MoveCategory(category, target.Before as SessionCategoryVM);
                break;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsDragging)
        {
            Cancel();
            e.Handled = true;
        }
    }

    private void Begin(IPointer pointer)
    {
        _draggedRow = _pressedRow;
        _draggedRow!.IsDragged = true;
        pointer.Capture(_list);

        _ghostText.Text = _draggedRow switch
        {
            ISessionVM session => session.Model.Name,
            SessionCategoryVM category => category.Name,
            _ => ""
        };
        _ghost.IsVisible = true;
        _scrollTimer.Start();
    }

    private void Cancel()
    {
        _pressedRow = null;
        if (_draggedRow != null)
            End();
    }

    private void End()
    {
        _scrollTimer.Stop();
        if (_draggedRow != null)
            _draggedRow.IsDragged = false;
        _draggedRow = null;
        _target = null;
        Highlight(null);
        _ghost.IsVisible = false;
        _indicator.IsVisible = false;

        if (_pointer?.Captured == _list)
            _pointer.Capture(null);
        _pointer = null;
    }

    private void Update()
    {
        Canvas.SetLeft(_ghost, Math.Max(0, _lastPoint.X + 12));
        Canvas.SetTop(_ghost, _lastPoint.Y - 14);

        _target = _draggedRow is SessionCategoryVM ? FindCategoryTarget() : FindSessionTarget();

        if (_target?.LineY is { } y)
        {
            Canvas.SetLeft(_indicator, 8);
            Canvas.SetTop(_indicator, y - 1);
            _indicator.Width = Math.Max(0, _list.Bounds.Width - 16);
            _indicator.IsVisible = true;
        }
        else
        {
            _indicator.IsVisible = false;
        }
    }

    private Target? FindSessionTarget()
    {
        var viewModel = _viewModel();
        var rows = VisibleRows();
        if (viewModel == null || rows.Count == 0)
            return null;

        var y = _lastPoint.Y;
        for (var i = 0; i < rows.Count; i++)
        {
            var (row, top, height) = rows[i];
            if (y >= top + height)
                continue;

            if (row is SessionCategoryVM category)
            {
                // The lower half of a header takes the session into the category, at its top
                if (y >= top + height / 2)
                {
                    Highlight(category);
                    var first = rows.Skip(i + 1).Select(r => r.Row)
                        .TakeWhile(r => r is ISessionVM).FirstOrDefault();
                    return new Target(category.Id, first, null);
                }

                // The upper half puts it at the end of what comes before the header
                Highlight(null);
                var previous = i > 0 ? rows[i - 1].Row : null;
                return new Target(previous == null ? null : viewModel.CategoryOf(previous), null, top);
            }

            Highlight(null);
            var categoryId = viewModel.CategoryOf(row);
            if (y < top + height / 2)
                return new Target(categoryId, row, top);

            // Below the middle: in front of the next session of the same place, or at its end
            var next = i + 1 < rows.Count && rows[i + 1].Row is ISessionVM nextSession
                                          && viewModel.CategoryOf(nextSession) == categoryId
                ? rows[i + 1].Row
                : null;
            return new Target(categoryId, next, top + height);
        }

        // Below every row: the end of the last place in the list
        Highlight(null);
        var (lastRow, lastTop, lastHeight) = rows[^1];
        return new Target(viewModel.CategoryOf(lastRow), null, lastTop + lastHeight);
    }

    private Target? FindCategoryTarget()
    {
        Highlight(null);
        var rows = VisibleRows();
        if (rows.Count == 0)
            return null;

        var headers = rows.Where(r => r.Row is SessionCategoryVM).ToList();
        foreach (var (row, top, height) in headers)
        {
            if (_lastPoint.Y < top + height / 2)
                return new Target(null, row, top);
        }

        var (_, lastTop, lastHeight) = rows[^1];
        return new Target(null, null, lastTop + lastHeight);
    }

    /// <summary>
    /// The rows on screen in their order with their place in the list, without the dragged one.
    /// </summary>
    private List<(ISessionListRow Row, double Top, double Height)> VisibleRows()
    {
        var rows = new List<(ISessionListRow, double, double)>();
        if (_viewModel() is not { } viewModel)
            return rows;

        for (var i = 0; i < viewModel.Rows.Count; i++)
        {
            var row = viewModel.Rows[i];
            if (row == _draggedRow || _list.ContainerFromIndex(i) is not { IsVisible: true } container)
                continue;

            if (container.TranslatePoint(default, _list) is not { } origin)
                continue;

            rows.Add((row, origin.Y, container.Bounds.Height));
        }

        return rows;
    }

    private void Highlight(SessionCategoryVM? category)
    {
        if (_highlighted == category)
            return;

        if (_highlighted != null)
            _highlighted.IsDropTarget = false;
        _highlighted = category;
        if (category != null)
            category.IsDropTarget = true;
    }

    private void AutoScroll()
    {
        if (_draggedRow == null || _list.FindDescendantOfType<ScrollViewer>() is not { } scroll)
            return;

        var height = _list.Bounds.Height;
        double step = 0;
        if (_lastPoint.Y < ScrollZone)
            step = -ScrollSpeed * (1 - Math.Max(0, _lastPoint.Y) / ScrollZone);
        else if (_lastPoint.Y > height - ScrollZone)
            step = ScrollSpeed * (1 - Math.Max(0, height - _lastPoint.Y) / ScrollZone);

        if (step == 0)
            return;

        var max = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        var offset = Math.Clamp(scroll.Offset.Y + step, 0, max);
        if (Math.Abs(offset - scroll.Offset.Y) < 0.5)
            return;

        scroll.Offset = scroll.Offset.WithY(offset);
        Update();
    }

    private static Border? FindHeader(Visual source) =>
        source.GetSelfAndVisualAncestors().OfType<Border>()
            .FirstOrDefault(border => border.Classes.Contains("sessionHeader") || border.Classes.Contains("categoryHeader"));

    private ISessionListRow? RowOf(Visual source) =>
        source.GetSelfAndVisualAncestors().OfType<ListBoxItem>()
            .FirstOrDefault(item => ItemsControl.ItemsControlFromItemContainer(item) == _list)?.DataContext as ISessionListRow;
}
