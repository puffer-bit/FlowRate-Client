using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.Views.Main;

namespace Client.Views.Common;

/// <summary>
/// The minimize, maximize and close buttons of the window the control is in, for a window that draws
/// its own title bar. Hidden where the system still draws the title bar, and in fullscreen.
/// </summary>
internal partial class WindowCaptionButtons : UserControl
{
    private Window? _window;

    public WindowCaptionButtons()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window != null)
            _window.PropertyChanged += OnWindowPropertyChanged;

        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_window != null)
            _window.PropertyChanged -= OnWindowPropertyChanged;
        _window = null;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == Window.IsExtendedIntoWindowDecorationsProperty)
            Update();
    }

    private void Update()
    {
        IsVisible = _window is { IsExtendedIntoWindowDecorations: true, WindowState: not WindowState.FullScreen };

        var isMaximized = _window?.WindowState == WindowState.Maximized;
        MaximizeButton.IsVisible = !isMaximized;
        RestoreButton.IsVisible = isMaximized;
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        if (_window != null)
            _window.WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        if (_window != null)
            _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        // The main window may hide to the tray or ask instead of closing
        if (_window is MainWindow mainWindow)
            mainWindow.RequestClose();
        else
            _window?.Close();
    }
}
