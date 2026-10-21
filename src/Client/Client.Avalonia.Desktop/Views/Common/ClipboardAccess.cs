using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace Client.Views.Common;

internal static class ClipboardAccess
{
    /// <summary>
    /// Puts <paramref name="text"/> on the clipboard of the window that hosts <paramref name="source"/>.
    /// Without a source the clipboard of the main window is taken, which is what a menu needs: its popup
    /// is on its way out by the time its item is clicked.
    /// </summary>
    public static async Task SetTextAsync(Visual? source, string text)
    {
        var topLevel = source != null ? TopLevel.GetTopLevel(source) : MainWindow;

        if (topLevel?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    private static TopLevel? MainWindow =>
        (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
