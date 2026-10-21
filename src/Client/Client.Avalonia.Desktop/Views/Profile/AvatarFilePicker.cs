using Client.Avalonia.Localization;
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ReactiveUI;
using Client.ViewModels.Profile;
using Client.ViewModels.Sessions.Chat;

namespace Client.Views.Profile;

/// <summary>
/// Answers <see cref="AvatarPickerVM.PickAvatarFile"/> with the file picker of the window the view is in.
/// </summary>
internal static class AvatarFilePicker
{
    private static FilePickerFileType ImageFiles => new(Strings.Get("profile.avatar.fileType"))
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"],
        MimeTypes = ["image/png", "image/jpeg", "image/webp", "image/bmp", "image/gif"]
    };

    public static IDisposable Register(AvatarPickerVM picker, Control view) =>
        picker.PickAvatarFile.RegisterHandler(async interaction =>
        {
            var storage = TopLevel.GetTopLevel(view)?.StorageProvider;
            var files = storage == null
                ? []
                : await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Strings.Get("profile.avatar.pickerTitle"),
                    AllowMultiple = false,
                    FileTypeFilter = [ImageFiles]
                });

            interaction.SetOutput((await PickedFiles.FromAsync(files)).FirstOrDefault());
        });
}
