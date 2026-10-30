using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ReactiveUI.Primitives;
using Client.ViewModels.Settings.Pages;
using Client.Views.Common;
using Client.Views.Dialogs;
using ReactiveUI;

namespace Client.Views.Settings.Pages;

public partial class DebugAndLogsControl : ViewControl<DebugAndLogsControlVM>
{
    public DebugAndLogsControl()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            ViewModel!.ShowMessageBox.RegisterMessageBoxHandler(this).DisposeWith(disposables);
            ViewModel!.PickArchiveTarget.RegisterHandler(async interaction =>
                interaction.SetOutput(await PickArchiveTarget(interaction.Input))).DisposeWith(disposables);
        });
    }

    private async Task<Stream?> PickArchiveTarget(string fileName)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null)
            return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = fileName,
            DefaultExtension = "zip",
            ShowOverwritePrompt = true
        });

        return file == null ? null : await file.OpenWriteAsync();
    }

    private async void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.LogDirectory is not { } path || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        try
        {
            await topLevel.Launcher.LaunchDirectoryInfoAsync(Directory.CreateDirectory(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing to open: the folder cannot be created here
        }
    }
}
