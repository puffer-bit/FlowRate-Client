using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.ViewModels.Settings.Pages;

namespace Client.Views.Dialogs;

internal partial class EncoderSettingsDialog : UserControl
{
    public EncoderSettingsDialog() => InitializeComponent();

    private void Reset(object? sender, RoutedEventArgs e) => (DataContext as EncoderSettingsVM)?.Reset();
    private void Cancel(object? sender, RoutedEventArgs e) => InWindowDialog.Close(this, false);
    private async void Save(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EncoderSettingsVM vm && await vm.SaveAsync())
            InWindowDialog.Close(this, true);
    }
}
