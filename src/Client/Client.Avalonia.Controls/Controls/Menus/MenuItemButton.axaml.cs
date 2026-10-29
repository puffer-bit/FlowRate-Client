using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;

namespace Client.Avalonia.Controls.Menus;

/// <summary>
/// An item of a flyout menu: an icon, the label (the button content) and, on the right, the keys of
/// <see cref="Gesture"/> drawn as keycaps. The gesture is only shown; it is not registered as a hot key.
/// Add the "danger" class for destructive actions.
/// An item with a <see cref="Button.Flyout"/> is a submenu: it shows an arrow, opens the flyout when
/// the pointer rests on it or on the right arrow key, and the other items of its menu close it again.
/// </summary>
[PseudoClasses(SubmenuPseudoClass)]
public class MenuItemButton : Button
{
    private const string SubmenuPseudoClass = ":submenu";

    // As long as the pointer has to rest on an item before its submenu opens
    private static readonly TimeSpan SubmenuDelay = TimeSpan.FromMilliseconds(250);

    private IDisposable? _submenuTimer;

    public static readonly StyledProperty<IImage?> IconProperty =
        AvaloniaProperty.Register<MenuItemButton, IImage?>(nameof(Icon));

    public static readonly StyledProperty<object?> IconContentProperty =
        AvaloniaProperty.Register<MenuItemButton, object?>(nameof(IconContent));

    public static readonly StyledProperty<KeyGesture?> GestureProperty =
        AvaloniaProperty.Register<MenuItemButton, KeyGesture?>(nameof(Gesture));

    public static readonly DirectProperty<MenuItemButton, IReadOnlyList<GestureKey>> GestureKeysProperty =
        AvaloniaProperty.RegisterDirect<MenuItemButton, IReadOnlyList<GestureKey>>(nameof(GestureKeys), o => o.GestureKeys);

    private IReadOnlyList<GestureKey> _gestureKeys = [];

    protected override Type StyleKeyOverride => typeof(MenuItemButton);

    public IImage? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// A control in the place of the <see cref="Icon"/>, for an icon that changes with its data.
    /// </summary>
    public object? IconContent
    {
        get => GetValue(IconContentProperty);
        set => SetValue(IconContentProperty, value);
    }

    public KeyGesture? Gesture
    {
        get => GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    /// <summary>The gesture split into its keycaps, modifiers first</summary>
    public IReadOnlyList<GestureKey> GestureKeys
    {
        get => _gestureKeys;
        private set => SetAndRaise(GestureKeysProperty, ref _gestureKeys, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == GestureProperty)
            GestureKeys = Split(Gesture);
        else if (change.Property == FlyoutProperty)
            PseudoClasses.Set(SubmenuPseudoClass, Flyout != null);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);

        CloseSiblingSubmenus();
        if (Flyout == null || Flyout.IsOpen)
            return;

        _submenuTimer?.Dispose();
        _submenuTimer = DispatcherTimer.RunOnce(OpenSubmenu, SubmenuDelay);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        // Left before it opened; an open submenu stays until another item of this menu is reached
        _submenuTimer?.Dispose();
        _submenuTimer = null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Right && Flyout != null)
        {
            OpenSubmenu();
            e.Handled = true;
            return;
        }

        // The left arrow inside a submenu goes back to the item it was opened from
        if (e.Key == Key.Left && this.FindLogicalAncestorOfType<Popup>() is { } popup
                              && popup.PlacementTarget is MenuItemButton { Flyout: { } parentSubmenu } parent)
        {
            parentSubmenu.Hide();
            parent.Focus(NavigationMethod.Directional);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void OpenSubmenu()
    {
        _submenuTimer?.Dispose();
        _submenuTimer = null;

        if (Flyout is not { IsOpen: false } submenu || !IsEffectivelyVisible)
            return;

        submenu.ShowAt(this);
    }

    // Reaching another item of the menu closes the submenus opened from this one's neighbours
    private void CloseSiblingSubmenus()
    {
        if (Parent is not Panel panel)
            return;

        foreach (var sibling in panel.Children.OfType<MenuItemButton>())
        {
            if (sibling != this && sibling.Flyout is { IsOpen: true } open)
                open.Hide();
        }
    }

    private static IReadOnlyList<GestureKey> Split(KeyGesture? gesture)
    {
        if (gesture == null)
            return [];

        var keys = new List<string>();
        var modifiers = gesture.KeyModifiers;
        if (modifiers.HasFlag(KeyModifiers.Control))
            keys.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Shift))
            keys.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Alt))
            keys.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Meta))
            keys.Add(OperatingSystem.IsMacOS() ? "Cmd" : "Win");
        if (gesture.Key != Key.None)
            keys.Add(KeyLabel(gesture.Key));
        return keys.Select((label, i) => new GestureKey(label, i == 0)).ToList();
    }

    private static string KeyLabel(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (char)('0' + (key - Key.NumPad0)),
        Key.Delete => "Del",
        Key.Escape => "Esc",
        Key.Back => "Backspace",
        Key.Return => "Enter",
        Key.PageUp => "PgUp",
        Key.PageDown => "PgDn",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.Left => "←",
        Key.Right => "→",
        Key.OemPlus => "+",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        _ => key.ToString()
    };
}

/// <summary>One keycap of a menu item gesture; every key but the first is preceded by a "+"</summary>
public sealed record GestureKey(string Label, bool IsFirst);
