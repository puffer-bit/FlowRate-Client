using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Client.Avalonia.Controls.Menus;

namespace Client.Services.Tray;

internal sealed partial class TrayService
{
    private void OnTrayMenuOpened(Window host, RoutedEventArgs args)
    {
        // Avalonia 12.1 hosts the Windows tray menu in this window. Keep its native tray
        // activation, positioning and dismiss behavior, replacing only the menu content.
        // Other platforms export NativeMenu to the desktop shell and keep that path.
        if (host.Name != "AvaloniaTrayPopupRoot_" + _trayIcon.ToolTipText ||
            host.Content is not MenuFlyoutPresenter)
            return;

        var items = new StackPanel { Spacing = 2 };
        var bindings = new CompositeDisposable();
        host.Closed += (_, _) => bindings.Dispose();
        items.Children.Add(CreateTrayItem(_openItem, "ExpandIcon", ShowWindow, host));
        items.Children.Add(new Separator());
        items.Children.Add(CreateTrayItem(_microphoneItem, "MicrophoneIcon",
            () => _session?.StatusBar.ToggleMicrophoneCommand.Execute().Subscribe(), host,
            "CrossedMicrophoneIcon"));
        items.Children.Add(CreateTrayItem(_soundItem, "HeadphonesIcon",
            () => _session?.StatusBar.ToggleSoundCommand.Execute().Subscribe(), host,
            "CrossedHeadphonesIcon"));
        items.Children.Add(CreateTrayItem(_afkItem, "AfkIcon",
            () => _session?.StatusBar.ToggleAfkCommand.Execute().Subscribe(), host, "AfkIcon"));
        var separator = new Separator();
        bindings.Add(separator.Bind(Visual.IsVisibleProperty,
            _voiceSeparator.GetObservable(NativeMenuItem.IsVisibleProperty)));
        items.Children.Add(separator);
        var exit = CreateTrayItem(_exitItem, "DoorWithRedArrowIcon", Exit, host);
        exit.Classes.Add("danger");
        items.Children.Add(exit);

        host.Content = new TrayMenuFlyout { Content = items }.BuildPresenter();
        host.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                host.Close();
                e.Handled = true;
            }
        };
        items.Children[0].Focus(NavigationMethod.Directional);
    }

    private static MenuItemButton CreateTrayItem(NativeMenuItem source, string icon,
        Action action, Window host, string? uncheckedIcon = null)
    {
        var button = new MenuItemButton();
        var bindings = new CompositeDisposable();
        host.Closed += (_, _) => bindings.Dispose();
        if (uncheckedIcon == null)
            bindings.Add(button.Bind(ContentControl.ContentProperty, source.GetObservable(NativeMenuItem.HeaderProperty)));
        bindings.Add(button.Bind(Visual.IsVisibleProperty, source.GetObservable(NativeMenuItem.IsVisibleProperty)));
        bindings.Add(button.Bind(InputElement.IsEnabledProperty, source.GetObservable(NativeMenuItem.IsEnabledProperty)));
        if (uncheckedIcon != null)
        {
            bindings.Add(button.Bind(MenuItemButton.IconProperty,
                source.GetObservable(NativeMenuItem.IsCheckedProperty).Select(isChecked =>
                    global::Avalonia.Application.Current!.FindResource(isChecked ? icon : uncheckedIcon) as IImage)));
            var mark = new Image { Width = 16, Height = 16 };
            bindings.Add(mark.Bind(Image.SourceProperty, new DynamicResourceExtension("MarkIcon")));
            bindings.Add(mark.Bind(Visual.IsVisibleProperty, source.GetObservable(NativeMenuItem.IsCheckedProperty)));
            // Keep the check separate from the shared icon column and label.
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
            var label = new TextBlock { VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
            bindings.Add(label.Bind(TextBlock.TextProperty, source.GetObservable(NativeMenuItem.HeaderProperty)));
            Grid.SetColumn(mark, 1);
            row.Children.Add(label);
            row.Children.Add(mark);
            button.Content = row;
        }
        else
        {
            bindings.Add(button.Bind(MenuItemButton.IconProperty, new DynamicResourceExtension(icon)));
        }
        button.Click += (_, _) =>
        {
            host.Close();
            action();
        };
        return button;
    }

    // Reuse Flyout's presenter creation and the application-wide FlyoutPresenter styles.
    // The tray already supplies the popup window, so no second popup is opened.
    private sealed class TrayMenuFlyout : Flyout
    {
        public Control BuildPresenter() => CreatePresenter();
    }
}
