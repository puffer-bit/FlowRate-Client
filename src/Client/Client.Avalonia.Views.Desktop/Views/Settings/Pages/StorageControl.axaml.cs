using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ReactiveUI.Primitives;
using Client.ViewModels.Settings.Pages;
using Client.Views.Common;
using Client.Views.Dialogs;
using ReactiveUI;

namespace Client.Views.Settings.Pages;

public partial class StorageControl : ViewControl<StorageControlVM>
{
    public StorageControl()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            ViewModel!.ShowMessageBox.RegisterMessageBoxHandler(this).DisposeWith(disposables);
        });
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
