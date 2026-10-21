using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Client.Views.Dialogs;

/// <summary>
/// Shows dialogs inside the window of the requesting view rather than as windows of their own:
/// on Wayland a window cannot be centered over its owner, tiling window managers turn it into a
/// separate tile, and it may end up behind a fullscreen session.
/// </summary>
internal static class InWindowDialog
{
    /// <summary>
    /// Shows <paramref name="content"/> over the window that hosts <paramref name="owner"/> until
    /// the content calls <see cref="Close"/> or the user dismisses it.
    /// </summary>
    /// <param name="dismissedResult">Returned on Escape, and when the window closes underneath.</param>
    /// <param name="fillWindow">See <see cref="DialogOverlay(Avalonia.Controls.Control,bool)"/>.</param>
    /// <returns>The result passed to <see cref="Close"/>, or <paramref name="dismissedResult"/>.</returns>
    public static async Task<TResult> ShowAsync<TResult>(Control content, Visual? owner, TResult dismissedResult,
        bool fillWindow = false)
    {
        var topLevel = FindOwnerTopLevel(owner);
        var overlayLayer = topLevel == null ? null : OverlayLayer.GetOverlayLayer(topLevel);

        if (topLevel == null || overlayLayer == null)
            throw new InvalidOperationException("There is no visible window to show the dialog in");
        
        var overlay = new DialogOverlay(content, fillWindow);
        var result = new TaskCompletionSource<TResult>();
        
        overlay.Closed += value => result.TrySetResult(value is TResult typed ? typed : dismissedResult);
        overlay.Dismissed += () => result.TrySetResult(dismissedResult);
        
        // The overlay layer does not stretch its children, so the backdrop follows its size
        using var size = overlayLayer.GetObservable(Visual.BoundsProperty).Subscribe(bounds =>
        {
            overlay.Width = bounds.Width;
            overlay.Height = bounds.Height;
        });
        
        // Whoever awaits the answer must not hang when the window goes away underneath
        void OnWindowClosed(object? sender, EventArgs e) => result.TrySetResult(dismissedResult);
        var window = topLevel as Window;
        
        if (window != null)
            window.Closed += OnWindowClosed;
        
        var previousFocus = topLevel.FocusManager?.GetFocusedElement() as InputElement;
        overlayLayer.Children.Add(overlay);

        try
        {
            return await result.Task;
        }
        finally
        {
            // A closed window is gone already, there is nothing left to animate
            if (window is not { IsVisible: false })
                await overlay.HideAsync();
            
            overlayLayer.Children.Remove(overlay);
            
            if (window != null)
                window.Closed -= OnWindowClosed;
            
            previousFocus?.Focus();
        }
    }

    /// <summary>
    /// Closes the dialog that contains <paramref name="content"/> with <paramref name="result"/>.
    /// </summary>
    public static void Close(Visual content, object? result = null)
    {
        content.FindAncestorOfType<DialogOverlay>()?.Close(result);
    }

    /// <summary>
    /// Whether a dialog can be shown for <paramref name="owner"/>, i.e. some window is visible.
    /// </summary>
    public static bool CanShow(Visual? owner) => FindOwnerTopLevel(owner) != null;

    private static TopLevel? FindOwnerTopLevel(Visual? owner)
    {
        if (owner != null && TopLevel.GetTopLevel(owner) is { IsVisible: true } topLevel)
            return topLevel;

        var lifetime = global::Avalonia.Application.Current?.ApplicationLifetime 
            as IClassicDesktopStyleApplicationLifetime;
        
        return lifetime?.MainWindow is { IsVisible: true } mainWindow ? mainWindow : null;
    }
}
