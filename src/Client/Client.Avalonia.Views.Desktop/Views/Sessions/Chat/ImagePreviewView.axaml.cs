using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Client.Views.Shared.Common;

namespace Client.Views.Sessions.Chat;

/// <summary>One image viewer, hosted over the chat or in an explicit fullscreen window.</summary>
internal partial class ImagePreviewView : UserControl
{
    private Window? _fullscreen;
    private Action? _leaveFullscreen;
    private ImagePreviewVM? Preview => DataContext as ImagePreviewVM;

    public ImagePreviewView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => { if (Preview is { } preview) preview.Presentation = this; };
        DetachedFromVisualTree += (_, _) => _fullscreen?.Close();
    }

    internal void EnterFullscreen() => ToggleFullscreen(this, new RoutedEventArgs());

    private void ToggleFullscreen(object? sender, RoutedEventArgs e)
    {
        if (_leaveFullscreen != null) { _leaveFullscreen(); return; }
        if (_fullscreen != null || Preview == null || TopLevel.GetTopLevel(this) is not Window owner) return;
        var window = new Window { Title = Preview.FileName };
        window.Content = new ImagePreviewView { DataContext = Preview, _leaveFullscreen = window.Close };
        _fullscreen = window;
        IsVisible = false;
        var subscription = Preview.CloseCommand.Subscribe(_ => window.Close());
        window.Closed += (_, _) => { subscription.Dispose(); _fullscreen = null; IsVisible = true; if (Preview is { } preview) preview.Presentation = this; Focus(); };
        window.AddHandler(KeyDownEvent, (_, key) =>
        {
            if (key.Key == Key.Escape) { window.Close(); key.Handled = true; }
        }, RoutingStrategies.Tunnel);
        FullscreenWindow.Show(window, owner);
    }
}
