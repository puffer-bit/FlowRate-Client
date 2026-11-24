using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Client.Avalonia.Styles.Animation;
using Client.Avalonia.Controls.Menus;

namespace Client.Droid.Views.Common;

/// <summary>
/// The menu of the application: a card that grows out of where the finger was, over everything else,
/// and shrinks back into it when it is closed. It lies in a layer that covers the screen; a tap beside
/// the card dismisses it. It stays in the tree while closed, so that it can be animated both ways, and
/// takes no pointer input then.
/// </summary>
/// <remarks>
/// A menu is opened at <see cref="Anchor"/>, or where the screen was last pressed when there is none:
/// the press that asked for the menu, on a button or a held item. Menus written next to what they act
/// on are attached with <see cref="PopupMenus"/> and shown by one menu of the layer.
/// </remarks>
public class PopupMenu : ContentControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<PopupMenu, bool>(nameof(IsOpen));

    /// <summary>What the card grows out of, in the coordinates of the menu: a control or a point.</summary>
    public static readonly StyledProperty<Rect> AnchorProperty =
        AvaloniaProperty.Register<PopupMenu, Rect>(nameof(Anchor));

    /// <summary>
    /// Whether choosing an item closes the menu: an action does, a toggle (an item with a check mark)
    /// or a submenu does not. Menus that decide it in code leave it off.
    /// </summary>
    public static readonly StyledProperty<bool> ClosesOnItemClickProperty =
        AvaloniaProperty.Register<PopupMenu, bool>(nameof(ClosesOnItemClick));

    // Where the screen was pressed last, in the coordinates of the root that is watched
    private static Visual? _pressRoot;
    private static Point _lastPress;

    private Border? _backdrop;
    // Set by OpenAt for the opening that follows; any other opening is at the last press
    private bool _isAnchorGiven;
    private IDisposable? _hiding;

    static PopupMenu()
    {
        IsOpenProperty.Changed.AddClassHandler<PopupMenu>((menu, _) => menu.OnIsOpenChanged());
    }

    public PopupMenu()
    {
        IsVisible = false;
        AddHandler(Button.ClickEvent, OnItemClick, RoutingStrategies.Bubble);
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public Rect Anchor
    {
        get => GetValue(AnchorProperty);
        set => SetValue(AnchorProperty, value);
    }

    public bool ClosesOnItemClick
    {
        get => GetValue(ClosesOnItemClickProperty);
        set => SetValue(ClosesOnItemClickProperty, value);
    }

    /// <summary>
    /// Raised by a tap beside the card. The owner closes the menu: it may have more to undo than that.
    /// </summary>
    public event Action? Dismissed;

    /// <summary>
    /// Keeps where the screen is pressed, for the menus to open there. Called once with the root view.
    /// </summary>
    public static void TrackPresses(Visual root)
    {
        _pressRoot = root;
        if (root is InputElement element)
            element.AddHandler(PointerPressedEvent, (_, e) =>
            {
                _lastPress = e.GetPosition(root);
                PopupMenus.ForgetHeldButton();
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Opens the menu next to the given control, below it or above when there is no room.</summary>
    public void OpenAt(Visual target)
    {
        if (target.TransformToVisual(this) is { } transform)
        {
            Anchor = new Rect(target.Bounds.Size).TransformToAABB(transform);
            // An open menu only moves; a closed one opens there
            _isAnchorGiven = !IsOpen;
        }
        IsOpen = true;
    }

    /// <returns>False when the menu was not open.</returns>
    public bool TryClose()
    {
        if (!IsOpen)
            return false;

        IsOpen = false;
        return true;
    }

    private void OnIsOpenChanged()
    {
        PseudoClasses.Set(":open", IsOpen);
        MenuPresence.Follow(this, IsOpen, ref _hiding);
        if (!IsOpen)
            return;

        // Opened by setting the property: from the press that asked for it
        if (!_isAnchorGiven && _pressRoot != null && _pressRoot.TranslatePoint(_lastPress, this) is { } point)
            Anchor = new Rect(point, default(Size));
        _isAnchorGiven = false;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_backdrop != null)
            _backdrop.Tapped -= OnBackdropTapped;

        _backdrop = e.NameScope.Find<Border>("PART_Backdrop");
        if (_backdrop != null)
            _backdrop.Tapped += OnBackdropTapped;
    }

    private void OnBackdropTapped(object? sender, TappedEventArgs e) => Dismissed?.Invoke();

    private void OnItemClick(object? sender, RoutedEventArgs e)
    {
        if (!IsOpen || e.Source is not Button button)
            return;

        var closes = FlyoutMenu.GetCloseOnClick(button)
                     || (ClosesOnItemClick && button is MenuItemButton item
                         && item.Flyout == null && !item.IsSet(MenuItemButton.IsCheckedProperty));
        if (closes)
            IsOpen = false;
    }
}

/// <summary>
/// Lays out the card of a <see cref="PopupMenu"/>: next to its anchor, on the side of the screen the
/// anchor is on, below it or above when there is no room, and always inside the screen. The card grows
/// out of the corner that is at the anchor.
/// </summary>
public class PopupMenuPanel : Panel
{
    private const double ScreenMargin = 8;
    private const double Gap = 4;

    public static readonly StyledProperty<Rect> AnchorProperty =
        PopupMenu.AnchorProperty.AddOwner<PopupMenuPanel>();

    static PopupMenuPanel()
    {
        AffectsArrange<PopupMenuPanel>(AnchorProperty);
    }

    public Rect Anchor
    {
        get => GetValue(AnchorProperty);
        set => SetValue(AnchorProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var room = new Size(
            Math.Max(0, availableSize.Width - 2 * ScreenMargin),
            Math.Max(0, availableSize.Height - 2 * ScreenMargin));
        foreach (var child in Children)
            child.Measure(room);

        // The layer covers what it is given
        return new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var anchor = Anchor;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            var gap = anchor.Width > 0 || anchor.Height > 0 ? Gap : 0;

            // On the right half of the screen the card ends at the anchor, on the left it starts there
            var alignRight = anchor.Center.X > finalSize.Width / 2;
            var x = alignRight ? anchor.Right - size.Width : anchor.X;
            x = Math.Clamp(x, ScreenMargin, Math.Max(ScreenMargin, finalSize.Width - ScreenMargin - size.Width));

            var above = anchor.Bottom + gap + size.Height > finalSize.Height - ScreenMargin
                        && anchor.Y - gap - size.Height >= ScreenMargin;
            var y = above ? anchor.Y - gap - size.Height : anchor.Bottom + gap;
            y = Math.Clamp(y, ScreenMargin, Math.Max(ScreenMargin, finalSize.Height - ScreenMargin - size.Height));

            child.RenderTransformOrigin = new RelativePoint(alignRight ? 1 : 0, above ? 1 : 0, RelativeUnit.Relative);
            child.Arrange(new Rect(new Point(x, y), size));
        }

        return finalSize;
    }
}

/// <summary>
/// Menus written next to what they act on, as a flyout is: <c>Menu</c> opens with a tap on a button,
/// <c>ContextMenu</c> with a press and hold on any control. Both are shown by <see cref="Host"/>, the
/// menu of the root layer, with the data context of the control.
/// </summary>
public static class PopupMenus
{
    public static readonly AttachedProperty<Control?> MenuProperty =
        AvaloniaProperty.RegisterAttached<Button, Control?>("Menu", typeof(PopupMenus));

    public static readonly AttachedProperty<Control?> ContextMenuProperty =
        AvaloniaProperty.RegisterAttached<Control, Control?>("ContextMenu", typeof(PopupMenus));

    /// <summary>The menu that shows the attached ones; set by the root view.</summary>
    public static PopupMenu? Host { get; set; }

    // The button that was held for a menu: letting it go clicks it, and that click must not act too
    private static Button? _heldButton;

    static PopupMenus()
    {
        // Class handlers come before the handlers of the button and its command
        Button.ClickEvent.AddClassHandler<Button>((button, e) =>
        {
            if (!ReferenceEquals(button, _heldButton))
                return;

            _heldButton = null;
            e.Handled = true;
        });

        MenuProperty.Changed.AddClassHandler<Button>((button, e) =>
        {
            button.Click -= OnButtonClick;
            if (e.NewValue is Control)
                button.Click += OnButtonClick;
        });

        ContextMenuProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            control.RemoveHandler(Control.ContextRequestedEvent, OnContextRequested);
            if (e.NewValue is Control)
                control.AddHandler(Control.ContextRequestedEvent, OnContextRequested);
        });
    }

    public static Control? GetMenu(Button button) => button.GetValue(MenuProperty);
    public static void SetMenu(Button button, Control? value) => button.SetValue(MenuProperty, value);

    public static Control? GetContextMenu(Control control) => control.GetValue(ContextMenuProperty);
    public static void SetContextMenu(Control control, Control? value) => control.SetValue(ContextMenuProperty, value);

    /// <summary>Shows the given content in the menu of the layer, next to the control.</summary>
    public static void Show(Control target, Control content, bool atPress = false)
    {
        if (Host is not { } host)
            return;

        host.IsOpen = false;
        host.DataContext = target.DataContext;
        host.Content = content;
        if (atPress)
            host.IsOpen = true;
        else
            host.OpenAt(target);
    }

    private static void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && GetMenu(button) is { } content)
            Show(button, content);
    }

    private static void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Handled || sender is not Control control || GetContextMenu(control) is not { } content)
            return;

        e.Handled = true;
        _heldButton = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault();
        Show(control, content, atPress: true);
    }

    /// <summary>A new press starts clean, whatever became of the last one.</summary>
    internal static void ForgetHeldButton() => _heldButton = null;
}

/// <summary>
/// Keeps a closed menu out of the layout: it is shown when it opens and hidden once it has finished
/// closing, so that nothing of it is measured while nobody sees it.
/// </summary>
internal static class MenuPresence
{
    public static void Follow(Control menu, bool isOpen, ref IDisposable? hiding)
    {
        hiding?.Dispose();
        hiding = null;
        if (isOpen)
        {
            menu.IsVisible = true;
            return;
        }

        var closing = Motion.Get(MotionDuration.Medium);
        if (closing <= TimeSpan.Zero)
        {
            menu.IsVisible = false;
            return;
        }

        hiding = DispatcherTimer.RunOnce(() => menu.IsVisible = false, closing);
    }
}
