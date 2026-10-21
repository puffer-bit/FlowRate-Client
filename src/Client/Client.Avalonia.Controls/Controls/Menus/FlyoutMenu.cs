using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;

namespace Client.Avalonia.Controls.Menus;

/// <summary>
/// Menus in the app are a Flyout with MenuItemButton items rather than a MenuFlyout, so they look like
/// the rest of the flyouts. A Flyout does not close when an item is chosen, which is right for
/// toggles like mute and wrong for actions like delete; actions opt in with this property.
/// </summary>
public static class FlyoutMenu
{
    public static readonly AttachedProperty<bool> CloseOnClickProperty =
        AvaloniaProperty.RegisterAttached<Button, bool>("CloseOnClick", typeof(FlyoutMenu));

    static FlyoutMenu()
    {
        CloseOnClickProperty.Changed.AddClassHandler<Button>((button, e) =>
        {
            if (e.NewValue is true)
                button.Click += CloseFlyout;
            else
                button.Click -= CloseFlyout;
        });
    }

    public static bool GetCloseOnClick(Button button) => button.GetValue(CloseOnClickProperty);
    public static void SetCloseOnClick(Button button, bool value) => button.SetValue(CloseOnClickProperty, value);

    private static void CloseFlyout(object? sender, RoutedEventArgs e)
    {
        // The flyout content sits in a popup, which the flyout watches and closes along with it.
        // Click comes before the button runs its command, and a closed flyout takes the DataContext
        // from its content, so closing right away would leave the command bound to it null.
        // An item of a submenu closes the menus it was opened from too.
        var popups = (sender as Button)?.GetLogicalAncestors().OfType<Popup>().ToList();
        if (popups is { Count: > 0 })
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var popup in popups)
                    popup.Close();
            });
    }
}
