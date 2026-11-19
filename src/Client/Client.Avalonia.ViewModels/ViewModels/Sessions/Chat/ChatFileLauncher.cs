using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>Opens a cached attachment with the application's platform file launcher.</summary>
public static class ChatFileLauncher
{
    public static async Task<bool> OpenAsync(Control owner, string path)
    {
        try
        {
            if (TopLevel.GetTopLevel(owner) is not { } topLevel)
                return false;

            var file = await topLevel.StorageProvider.TryGetFileFromPathAsync(new Uri(Path.GetFullPath(path)));
            return file != null && await topLevel.Launcher.LaunchFileAsync(file);
        }
        catch (Exception)
        {
            // A missing association or platform launcher failure is shown on the file card.
            return false;
        }
    }
}
