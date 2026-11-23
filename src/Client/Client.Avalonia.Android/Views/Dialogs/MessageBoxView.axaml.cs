using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Client.ViewModels.Dialogs;

namespace Client.Droid.Views.Dialogs;

public partial class MessageBoxView : UserControl
{
    public MessageBoxView()
    {
        InitializeComponent();
    }

    private async void OnCopyDetailClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MessageBoxVM { CopyText: { } text } && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}
