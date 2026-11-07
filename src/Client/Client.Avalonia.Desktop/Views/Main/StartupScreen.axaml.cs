using Avalonia.Controls;
using Avalonia.Input;

namespace Client.Views.Main;

/// <summary>
/// What the main window shows over everything while the application starts: the theme-aware logo,
/// a ring and the current startup stage. Fades out once the application is loaded.
/// </summary>
internal partial class StartupScreen : UserControl
{
    public StartupScreen()
    {
        InitializeComponent();
    }

    private void OnContinueClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is Client.ViewModels.Main.MainWindowVM vm)
            vm.ContinueStartup();
    }

    private void OnExitClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is
            global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    // The screen covers the title bar the window may draw itself, so the window is moved by the screen
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.Handled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && TopLevel.GetTopLevel(this) is Window window)
            window.BeginMoveDrag(e);
    }
}
