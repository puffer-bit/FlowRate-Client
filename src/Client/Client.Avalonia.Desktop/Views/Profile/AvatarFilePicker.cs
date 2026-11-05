using Client.Avalonia.Localization;
using System;
using System.Linq;
using System.Reactive.Disposables;
using Client.Avalonia.Controls.Avatars;
using Client.Services;
using Client.Views.Dialogs;
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
        new CompositeDisposable(
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
            }),
            picker.EditAvatar.RegisterHandler(async interaction =>
            {
                var session = interaction.Input;
                var editor = new AvatarEditor();
                editor.SetPicture(session.Picture);
                editor.TransformRequested += mirror =>
                {
                    session.Transform(mirror);
                    editor.SetPicture(session.Picture);
                };
                editor.ResetRequested += () =>
                {
                    session.Reset();
                    editor.SetPicture(session.Picture);
                };
                editor.Accepted += () => InWindowDialog.Close(editor, session.Prepare(editor.Crop));
                editor.Cancelled += () => InWindowDialog.Close(editor);
                interaction.SetOutput(await InWindowDialog.ShowAsync<PreparedAvatar?>(editor, view, null));
            }));
}
