using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Client.Views.Shared.Common;

/// <summary>
/// Shows a window that covers its screen, such as a picture or a video opened to fullscreen.
/// </summary>
/// <remarks>
/// On macOS a window that has an owner cannot enter fullscreen, and a fullscreen state set before
/// the window is shown is ignored, which leaves an ordinary window beside the main one. There the
/// window is shown on its own, on the screen of its owner, and made fullscreen once it is open.
/// Leaving fullscreen with the system controls (the green button or Ctrl+Cmd+F) closes it, as
/// Escape does, and it closes with its owner.
/// </remarks>
public static class FullscreenWindow
{
    public static void Show(Window window, Window owner)
    {
        if (!OperatingSystem.IsMacOS())
        {
            window.WindowState = WindowState.FullScreen;
            window.Show(owner);
            return;
        }

        var entered = false;
        void OnOpened(object? sender, EventArgs e) =>
            Dispatcher.UIThread.Post(() => window.WindowState = WindowState.FullScreen);
        void OnStateChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != Window.WindowStateProperty)
                return;
            if (window.WindowState == WindowState.FullScreen)
                entered = true;
            else if (entered)
                window.Close();
        }
        void OnOwnerClosed(object? sender, EventArgs e) => window.Close();
        void OnClosed(object? sender, EventArgs e)
        {
            window.Opened -= OnOpened;
            window.PropertyChanged -= OnStateChanged;
            window.Closed -= OnClosed;
            owner.Closed -= OnOwnerClosed;
            // Without an owner the system does not return to the main window by itself
            if (owner.IsVisible)
                owner.Activate();
        }

        window.Opened += OnOpened;
        window.PropertyChanged += OnStateChanged;
        window.Closed += OnClosed;
        owner.Closed += OnOwnerClosed;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = owner.Position;
        window.Show();
    }
}
