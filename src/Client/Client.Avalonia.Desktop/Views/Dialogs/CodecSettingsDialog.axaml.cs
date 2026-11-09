using Avalonia.Controls;
using Avalonia.Interactivity;
using Client.ViewModels.Settings.Pages;

namespace Client.Views.Dialogs;

internal partial class CodecSettingsDialog : UserControl
{
    public CodecSettingsDialog() => InitializeComponent();

    private void Reset(object? sender, RoutedEventArgs e) => (DataContext as CodecSettingsVM)?.Reset();
    private void Cancel(object? sender, RoutedEventArgs e) => InWindowDialog.Close(this, false);
    private async void Save(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodecSettingsVM vm && await vm.SaveAsync())
            InWindowDialog.Close(this, true);
    }
}
