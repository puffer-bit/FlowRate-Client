using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Client.Views.Common;

/// <summary>
/// Makes a control the title bar of a window that draws its own: a press on its free space drags
/// the window and a double click maximizes or restores it. Does nothing where the system draws
/// the title bar.
/// </summary>
/// <remarks>
/// Not <c>WindowDecorationProperties.ElementRole="TitleBar"</c>: on X11 a press on such an element
/// never reaches the application, which leaves no way to tell a double click.
/// </remarks>
internal static class WindowDragArea
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(WindowDragArea));

    static WindowDragArea()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            control.PointerPressed -= OnPointerPressed;
            if (change.NewValue is true)
                control.PointerPressed += OnPointerPressed;
        });
    }

    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    // Buttons handle their presses, so only a press on the free space gets here
    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control
            || TopLevel.GetTopLevel(control) is not Window { IsExtendedIntoWindowDecorations: true } window
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2)
        {
            if (window.CanResize && window.CanMaximize && window.WindowState != WindowState.FullScreen)
                window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else if (e.ClickCount == 1)
        {
            window.BeginMoveDrag(e);
        }

        e.Handled = true;
    }
}
