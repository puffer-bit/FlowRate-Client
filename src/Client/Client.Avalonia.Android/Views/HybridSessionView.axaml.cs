using System;
using System.Collections.Generic;
using System.ComponentModel;
using Android.Content.PM;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Client.ViewModels.Sessions.Hybrid;
using Client.ViewModels.Sessions.List;

namespace Client.Droid.Views;

public partial class HybridSessionView : UserControl
{
    // The screen shown over the whole display, or null
    private ScreenStreamVM? _fullscreen;

    public HybridSessionView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Raised by a tap on someone in the session.
    /// </summary>
    public event Action<PeerVM>? PeerMenuRequested;

    /// <summary>
    /// Shows the peers of the session as the list of sessions has them, or nobody.
    /// </summary>
    public void ShowPeers(IEnumerable<PeerVM>? peers)
    {
        if (!ReferenceEquals(Participants.ItemsSource, peers))
            Participants.ItemsSource = peers;
    }

    private void OnParticipantTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not StyledElement { DataContext: PeerVM peer })
            return;

        PeerMenuRequested?.Invoke(peer);
        e.Handled = true;
    }

    private void OnPictureTapped(object? sender, TappedEventArgs e)
    {
        if (sender is StyledElement { DataContext: ScreenStreamVM stream })
            OpenFullscreen(stream);
    }

    private void OnFullscreenClick(object? sender, RoutedEventArgs e)
    {
        if (sender is StyledElement { DataContext: ScreenStreamVM stream })
            OpenFullscreen(stream);
    }

    private void OnCloseFullscreenClick(object? sender, RoutedEventArgs e) => TryCloseFullscreen();

    /// <summary>
    /// Shows a screen over the whole display, turned to landscape as a screen of a computer is.
    /// </summary>
    private void OpenFullscreen(ScreenStreamVM stream)
    {
        if (_fullscreen != null)
            _fullscreen.PropertyChanged -= OnFullscreenStreamChanged;

        _fullscreen = stream;
        stream.PropertyChanged += OnFullscreenStreamChanged;
        FullscreenPicture.Stream = stream;
        FullscreenLayer.IsVisible = true;

        if (MainActivity.Current is { } activity)
            activity.RequestedOrientation = ScreenOrientation.SensorLandscape;
    }

    /// <returns>False when no screen was shown over the display.</returns>
    public bool TryCloseFullscreen()
    {
        if (_fullscreen == null)
            return false;

        _fullscreen.PropertyChanged -= OnFullscreenStreamChanged;
        _fullscreen = null;
        FullscreenPicture.Stream = null;
        FullscreenLayer.IsVisible = false;

        if (MainActivity.Current is { } activity)
            activity.RequestedOrientation = ScreenOrientation.Unspecified;

        return true;
    }

    // A screen that is no longer watched has nothing to show over the display
    private void OnFullscreenStreamChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ScreenStreamVM { IsWatched: false } or ScreenStreamVM { IsLeaving: true })
            TryCloseFullscreen();
    }

    // The session left the display, and the display turns as it likes again
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        TryCloseFullscreen();
    }

    // Another session, or none: its screens go with it
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        TryCloseFullscreen();
    }
}
