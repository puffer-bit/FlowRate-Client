using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.VisualTree;
using Client.Application.Ports.Repositories;
using Client.Avalonia.Styles.Animation;
using Client.Views.Shared.Common;

namespace Client.Views.Common;

/// <summary>
/// The panel on the right of a session: the members and the pins of a chat, the peers of a hybrid session.
/// It slides open and shut, and the grip on its left edge resizes it: dragged left it widens, right it
/// narrows. Each kind of panel, by its <see cref="PanelKey"/>, keeps the width it was given between runs.
/// </summary>
public class SidePanelHost : Control
{
    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<SidePanelHost, object?>(nameof(Content));
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<SidePanelHost, bool>(nameof(IsOpen));
    public static readonly StyledProperty<string?> PanelKeyProperty =
        AvaloniaProperty.Register<SidePanelHost, string?>(nameof(PanelKey));

    private const double MinPanelWidth = 220, MaxPanelWidth = 480;

    // What the content beside the panel keeps at least, however far it is widened
    private const double ContentMinWidth = 320;

    private const double DefaultPanelWidth = 260;

    // The widths of the kinds of panels, loaded once from the storage; a panel without a key has the default
    private static readonly Dictionary<string, double> Widths = new();
    private static readonly Dictionary<string, Task> Loads = new();

    private double PanelWidth =>
        PanelKey is { } key && Widths.TryGetValue(key, out var width) ? width : DefaultPanelWidth;

    private readonly ContentControl _presenter;
    private readonly Border _grip;
    private readonly Border _gripLine;

    private double? _resizeStartX;
    private double _resizeStartWidth;

    [Content]
    public object? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }

    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

    /// <summary>The kind of panel, e.g. "chat" or "hybrid": the panels of a kind share their width.</summary>
    public string? PanelKey { get => GetValue(PanelKeyProperty); set => SetValue(PanelKeyProperty, value); }

    public SidePanelHost()
    {
        ClipToBounds = true;
        Width = 0;
        IsHitTestVisible = false;

        // Laid out at the full width from the left, so it is uncovered as the host widens
        _presenter = new ContentControl { HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left, Width = DefaultPanelWidth };

        // A thin line shows up under the pointer and while the panel is dragged
        _gripLine = new Border
        {
            Width = 2,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            Opacity = 0,
            IsHitTestVisible = false
        };
        _gripLine.Bind(Border.BackgroundProperty, this.GetResourceObservable("ButtonMainBrush"));

        _grip = new Border
        {
            Width = 6,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
            Child = _gripLine
        };
        _grip.PointerEntered += (_, _) => UpdateGripLine();
        _grip.PointerExited += (_, _) => UpdateGripLine();
        _grip.PointerPressed += OnGripPressed;
        _grip.PointerMoved += OnGripMoved;
        _grip.PointerReleased += OnGripReleased;
        _grip.PointerCaptureLost += (_, _) => EndResize();

        var root = new Panel { Children = { _presenter, _grip } };
        LogicalChildren.Add(root);
        VisualChildren.Add(root);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ContentProperty)
            _presenter.Content = change.NewValue;
        else if (change.Property == IsOpenProperty)
            ApplyOpen(animate: this.IsAttachedToVisualTree());
        else if (change.Property == PanelKeyProperty && change.NewValue is string key)
            _ = LoadWidthAsync(key);
    }

    private void ApplyOpen(bool animate)
    {
        Transitions = animate ? CreateTransitions() : null;
        _presenter.Width = PanelWidth;
        Width = IsOpen ? PanelWidth : 0;
        IsHitTestVisible = IsOpen;
    }

    // Read once for a kind of panel; the panels that come later find it in memory
    private async Task LoadWidthAsync(string key)
    {
        if (!Loads.TryGetValue(key, out var load))
            Loads[key] = load = ReadWidthAsync(key);

        await load;
        if (PanelKey == key && _resizeStartX == null)
            ApplyOpen(animate: false);
    }

    private static async Task ReadWidthAsync(string key)
    {
        try
        {
            if (ViewServices.Get<IPanelWidthsRepository>() is { } widths
                && await widths.GetAsync(key) is { } width)
                Widths.TryAdd(key, Math.Clamp(width, MinPanelWidth, MaxPanelWidth));
        }
        catch (Exception)
        {
            // The width is a convenience: without the storage the panel opens at the default one
        }
    }

    private static async Task SaveWidthAsync(string key, double width)
    {
        try
        {
            if (ViewServices.Get<IPanelWidthsRepository>() is { } widths)
                await widths.SetAsync(key, width);
        }
        catch (Exception)
        {
            // Kept for this run in memory all the same
        }
    }

    private static Transitions CreateTransitions() => new()
    {
        new DoubleTransition
        {
            Property = WidthProperty,
            Duration = Motion.Get(MotionDuration.Short),
            Easing = Motion.Ease(MotionEase.Standard)
        }
    };

    private void UpdateGripLine() =>
        _gripLine.Opacity = _grip.IsPointerOver || _resizeStartX != null ? 1 : 0;

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsOpen || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // The edge follows the pointer, without the easing of opening
        Transitions = null;
        _resizeStartX = e.GetPosition(Parent as Visual ?? this).X;
        _resizeStartWidth = Width;
        e.Pointer.Capture(_grip);
        UpdateGripLine();
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_resizeStartX is not { } startX)
            return;

        var room = (Parent as Control)?.Bounds.Width ?? double.PositiveInfinity;
        var maxWidth = Math.Max(MinPanelWidth, Math.Min(MaxPanelWidth, room - ContentMinWidth));
        var width = Math.Clamp(_resizeStartWidth - (e.GetPosition(Parent as Visual ?? this).X - startX),
            MinPanelWidth, maxWidth);

        Width = width;
        _presenter.Width = width;
        e.Handled = true;
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resizeStartX == null)
            return;

        e.Pointer.Capture(null);
        EndResize();
        e.Handled = true;
    }

    private void EndResize()
    {
        if (_resizeStartX == null)
            return;

        _resizeStartX = null;
        if (PanelKey is { } key)
        {
            Widths[key] = Width;
            _ = SaveWidthAsync(key, Width);
        }
        Transitions = CreateTransitions();
        UpdateGripLine();
    }
}
