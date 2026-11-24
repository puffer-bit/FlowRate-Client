using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Client.Avalonia.Localization;
using ReactiveUI;
using Client.Views.Common;
using ReactiveUI.Primitives;
using Client.Views.Dialogs;
using Client.ViewModels.Settings.Pages;

namespace Client.Views.Settings.Pages;

public partial class AccountingControl : ViewControl<AccountingControlVM>
{
    public AccountingControl()
    {
        InitializeComponent();
        
        this.WhenActivated(disposables =>
        {
            ViewModel!.ShowMessageBox.RegisterMessageBoxHandler(this).DisposeWith(disposables);
            ViewModel!.AskDatabasePassword.RegisterDatabasePasswordHandler(this).DisposeWith(disposables);
            ViewModel!.PickBackupTarget.RegisterHandler(async interaction =>
                interaction.SetOutput(await PickBackupTargetAsync(interaction.Input))).DisposeWith(disposables);
            ViewModel!.PickBackupSource.RegisterHandler(async interaction =>
                interaction.SetOutput(await PickBackupSourceAsync())).DisposeWith(disposables);
            ViewModel!.PickSyncFolder.RegisterHandler(async interaction =>
                interaction.SetOutput(await PickSyncFolderAsync())).DisposeWith(disposables);
            ViewModel!.AskRecoveryPhrase.RegisterHandler(async interaction =>
                interaction.SetOutput(await AskRecoveryPhraseAsync())).DisposeWith(disposables);
        });
    }

    private async Task<Stream?> PickBackupTargetAsync(string fileName)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return null;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = fileName,
            DefaultExtension = Client.Application.Sync.AccountBackupFile.Extension,
            ShowOverwritePrompt = true
        });
        return file == null ? null : await file.OpenWriteAsync();
    }

    private async Task<Stream?> PickBackupSourceAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("FlowRate backup") { Patterns = ["*." + Client.Application.Sync.AccountBackupFile.Extension] }]
        });
        return files.FirstOrDefault() is { } file ? await file.OpenReadAsync() : null;
    }

    private async Task<string?> PickSyncFolderAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return null;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    // The phrase is typed into a small dialog of its own; it goes no further than the backup it opens
    private async Task<string?> AskRecoveryPhraseAsync()
    {
        if (!InWindowDialog.CanShow(this))
            return null;

        var input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80 };
        var ok = new Button { Content = Strings.Get("common.ok"), Classes = { "primary" }, IsDefault = true };
        var cancel = new Button { Content = Strings.Get("common.cancel"), IsCancel = true };
        var form = new StackPanel
        {
            MaxWidth = 420,
            Margin = new Thickness(20, 16),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = Strings.Get("settings.backup.phraseTitle"), FontSize = 16, FontWeight = FontWeight.SemiBold
                },
                new TextBlock { Text = Strings.Get("settings.backup.phraseHint"), TextWrapping = TextWrapping.Wrap },
                input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, ok }
                }
            }
        };
        ok.Click += (_, _) => InWindowDialog.Close(form, input.Text);
        cancel.Click += (_, _) => InWindowDialog.Close(form);
        input.AttachedToVisualTree += (_, _) => input.Focus();
        return await InWindowDialog.ShowAsync<string?>(form, this, null);
    }
}
