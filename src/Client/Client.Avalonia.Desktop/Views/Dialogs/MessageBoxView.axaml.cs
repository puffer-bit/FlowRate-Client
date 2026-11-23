using Avalonia.Controls;

namespace Client.Views.Dialogs;

internal partial class MessageBoxView : UserControl
{
    private async void CopyDetail(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is Client.ViewModels.Dialogs.MessageBoxVM { CopyText: { } text })
            await Client.Views.Common.ClipboardAccess.SetTextAsync(this, text);
    }

    public MessageBoxView()
    {
        InitializeComponent();
    }
}
