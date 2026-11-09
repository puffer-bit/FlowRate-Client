using Avalonia.Interactivity;
using Avalonia.Controls;
using Client.Views.Dialogs;
using Client.ViewModels.Settings.Pages;
using Client.Views.Common;

namespace Client.Views.Settings.Pages;

public partial class VideoControl : ViewControl<VideoControlVM>
{
    private async void ConfigureCodec(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VideoControlVM vm || sender is not Control { DataContext: CodecSelectionVM selection })
            return;
        using var handler = vm.ShowMessageBox.RegisterMessageBoxHandler(this);
        if (await vm.OpenCodecSettingsAsync(selection) is { } settings)
            await InWindowDialog.ShowAsync(new CodecSettingsDialog { DataContext = settings }, this, false);
    }

    public VideoControl()
    {
        InitializeComponent();
    }
}
