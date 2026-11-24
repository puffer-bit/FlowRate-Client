using Avalonia.Controls;
using Avalonia.Input;
using Client.ViewModels.Notifications;

namespace Client.Views.Notifications;

public partial class ToastHost : UserControl
{
    public ToastHost()
    {
        InitializeComponent();
    }

    // A notification does not leave while the pointer is over it
    private void Toast_PointerEntered(object? sender, PointerEventArgs e)
    {
        if ((sender as Control)?.DataContext is ToastVM toast)
            toast.IsPointerOver = true;
    }

    private void Toast_PointerExited(object? sender, PointerEventArgs e)
    {
        if ((sender as Control)?.DataContext is ToastVM toast)
            toast.IsPointerOver = false;
    }
}
