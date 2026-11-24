using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.Views.Common;

namespace Client.Views.Sessions.Chat;

/// <summary>One image viewer, hosted over the chat or explicitly over the whole window in fullscreen.</summary>
internal partial class ImagePreviewView : UserControl
{
    private FullscreenHost? _fullscreen;
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
        if (_fullscreen != null || Preview == null) return;
        var view = new ImagePreviewView { DataContext = Preview };
        var fullscreen = FullscreenHost.Show(view, this);
        if (fullscreen == null) return;
        view._leaveFullscreen = fullscreen.Close;
        _fullscreen = fullscreen;
        IsVisible = false;
        var subscription = Preview.CloseCommand.Subscribe(_ => fullscreen.Close());
        fullscreen.Closed += () =>
        {
            subscription.Dispose();
            _fullscreen = null;
            IsVisible = true;
            if (Preview is { } preview) preview.Presentation = this;
            Focus();
        };
    }
}
