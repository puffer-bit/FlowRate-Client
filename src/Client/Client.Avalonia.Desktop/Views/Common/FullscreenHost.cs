using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Client.Views.Common;

/// <summary>
/// Shows content over the whole window that hosts a view instead of in a window of its own, and puts
/// that window into fullscreen while it does. Escape leaves.
/// </summary>
internal sealed class FullscreenHost
{
    private readonly TopLevel _topLevel;
    private readonly OverlayLayer _overlayLayer;
    private readonly Panel _container;
    private readonly IDisposable _size;
    private readonly Window? _window;
    private readonly WindowState? _restoreState;
    private readonly IInputElement? _previousFocus;
    private bool _isClosed;

    /// <summary>Raised once the content has left the window.</summary>
    public event Action? Closed;

    private FullscreenHost(TopLevel topLevel, OverlayLayer overlayLayer, Control content, Visual owner,
        IBrush? background)
    {
        _topLevel = topLevel;
        _overlayLayer = overlayLayer;
        _previousFocus = topLevel.FocusManager?.GetFocusedElement();

        // A background catches the pointer, so nothing under the content can be clicked
        _container = new Panel { Background = background ?? Brushes.Black, Focusable = true };
        // The content keeps the data context of the view it comes from, not that of the window
        if (owner is StyledElement { DataContext: { } dataContext })
            _container.DataContext = dataContext;
        _container.Children.Add(content);
        _container.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        // The overlay layer does not stretch its children
        _size = overlayLayer.GetObservable(Visual.BoundsProperty).Subscribe(bounds =>
        {
            _container.Width = bounds.Width;
            _container.Height = bounds.Height;
        });

        _window = topLevel as Window;
        if (_window != null)
        {
            _window.Closed += OnWindowClosed;

            // A window that is fullscreen already stays so once the content leaves
            if (_window.WindowState != WindowState.FullScreen)
            {
                _restoreState = _window.WindowState;
                _window.WindowState = WindowState.FullScreen;
            }
        }

        overlayLayer.Children.Add(_container);
        _container.Focus();
    }

    /// <summary>
    /// Shows <paramref name="content"/> over the window that hosts <paramref name="owner"/>.
    /// </summary>
    /// <returns>The host, to close it, or null when there is no window to show the content in.</returns>
    public static FullscreenHost? Show(Control content, Visual owner, IBrush? background = null)
    {
        var topLevel = FindTopLevel(owner);
        var overlayLayer = topLevel == null ? null : OverlayLayer.GetOverlayLayer(topLevel);

        return topLevel == null || overlayLayer == null
            ? null
            : new FullscreenHost(topLevel, overlayLayer, content, owner, background);
    }

    /// <summary>Takes the content out of the window and gives the window its previous state back.</summary>
    public void Close()
    {
        if (_isClosed)
            return;
        _isClosed = true;

        _size.Dispose();
        _overlayLayer.Children.Remove(_container);
        _container.Children.Clear();

        if (_window != null)
        {
            _window.Closed -= OnWindowClosed;
            if (_restoreState is { } state && _window.WindowState == WindowState.FullScreen)
                _window.WindowState = state;
        }

        Closed?.Invoke();
        _previousFocus?.Focus();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        Close();
    }

    private void OnWindowClosed(object? sender, EventArgs e) => Close();

    private static TopLevel? FindTopLevel(Visual owner)
    {
        // A popup closes as soon as it loses focus, the content goes to the window behind it
        if (TopLevel.GetTopLevel(owner) is { } topLevel and not PopupRoot)
            return topLevel;

        return global::Avalonia.Application.Current?.ApplicationLifetime switch
        {
            IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
            ISingleViewApplicationLifetime singleView => TopLevel.GetTopLevel(singleView.MainView),
            _ => null
        };
    }
}
