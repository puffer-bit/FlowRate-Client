using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ReactiveUI.Primitives;
using Client.ViewModels.Settings.Pages;
using Client.Views.Common;
using ReactiveUI;

namespace Client.Views.Settings.Pages;

public partial class DebugAndLogsControl : ViewControl<DebugAndLogsControlVM>
{
    public DebugAndLogsControl()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
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

}
