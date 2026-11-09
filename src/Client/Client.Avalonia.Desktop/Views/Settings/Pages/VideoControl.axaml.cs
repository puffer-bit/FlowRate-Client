using Avalonia.Interactivity;
using Client.Views.Dialogs;
using Client.ViewModels.Settings.Pages;
using Client.Views.Common;

namespace Client.Views.Settings.Pages;

public partial class VideoControl : ViewControl<VideoControlVM>
{
    private async void ConfigureEncoder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is VideoControlVM vm && vm.CreateEncoderSettings() is { } settings)
            await InWindowDialog.ShowAsync(new EncoderSettingsDialog { DataContext = settings }, this, false);
    }

    public VideoControl()
    {
        InitializeComponent();
    }
}
