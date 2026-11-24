using System.Threading.Tasks;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Input;
using Avalonia.Media.Imaging;

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

    public static async Task<bool> SetBitmapAsync(Visual? source, Bitmap bitmap)
    {
        var topLevel = source != null ? TopLevel.GetTopLevel(source) : MainWindow;
        if (topLevel?.Clipboard is not { } clipboard)
            return false;

        var png = await Task.Run(() =>
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            return stream.ToArray();
        });

        // Clipboard requests can arrive after the source bitmap is disposed or its tile is recycled.
        // Keep encoded pixels and provide a fresh bitmap for each request.
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DataFormat.Bitmap, () =>
        {
            using var content = new MemoryStream(png, writable: false);
            return new Bitmap(content);
        }));
        await clipboard.SetDataAsync(data);
        return true;
    }
}
