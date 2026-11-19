using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ReactiveUI;

namespace Client.ViewModels.Sessions.Chat;

public static class PickedFiles
{
    /// <summary>
    /// The files the platform gave, by the picker, a drop or the clipboard. Each is kept open to
    /// be read later, when the message is sent.
    /// </summary>
    public static async Task<IReadOnlyList<PickedFile>> FromAsync(IEnumerable<IStorageItem> items)
    {
        var picked = new List<PickedFile>();
        foreach (var item in items)
        {
            if (item is not IStorageFile file)
                continue;

            var properties = await file.GetBasicPropertiesAsync();
            var path = file.TryGetLocalPath();
            picked.Add(new PickedFile(file.Name, path ?? file.Path.ToString(),
                (long)(properties.Size ?? 0), path != null ? () => OpenShared(path, file) : file.OpenReadAsync));
        }

        return picked;
    }

    // Shared with the program that writes the file, as a log being written still is: the storage file of
    // Avalonia opens it for reading alone, which Windows refuses while another program writes to it.
    // Only what the file held when it was picked is sent. A path this process may not open itself, as
    // Android gives, is read the way the platform reads it
    private static async Task<Stream> OpenShared(string path, IStorageFile file)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                4096, useAsync: true);
        }
        catch (UnauthorizedAccessException)
        {
            return await file.OpenReadAsync();
        }
    }

    /// <summary>A picture that is not a file, as the clipboard holds a screenshot.</summary>
    public static PickedFile FromBitmap(Bitmap bitmap, string name)
    {
        using var encoded = new MemoryStream();
        bitmap.Save(encoded, PngBitmapEncoderOptions.Default);
        var bytes = encoded.ToArray();
        return new PickedFile(name, null, bytes.Length,
            () => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)));
    }

    public static FilePickerFileType AnyFileType(string name) => new(name)
    {
        Patterns = ["*"],
        MimeTypes = ["*/*"],
        AppleUniformTypeIdentifiers = ["public.item"]
    };

    public static FilePickerFileType ImageFileType(string name) => new(name)
    {
        Patterns = ChatComposerVM.ImageFilePatterns,
        MimeTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"],
        AppleUniformTypeIdentifiers = ["public.image"]
    };
}
