using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.Views.Dialogs;
using Client.Views.Common;
using Client.ViewModels.Connections;

namespace Client.Views.Connections;

internal partial class ConnectView : ViewControl<ConnectWindowVM>
{
    public ConnectView()
    {
        InitializeComponent();

        if (!Design.IsDesignMode)
        {
            this.WhenActivated(disposables =>
            {
                ViewModel!.CloseWindow.RegisterCloseHandler(this).DisposeWith(disposables);
                ViewModel!.ShowMessageBox.RegisterMessageBoxHandler(this).DisposeWith(disposables);
                ViewModel!.AskLogin.RegisterLoginPromptHandler(this).DisposeWith(disposables);
            });

            // Once the user is done with the address, a port typed into it moves to the port field
            AddressInput.LostFocus += (_, _) => ViewModel?.NormalizeAddress();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        
        // The dialog is gone, however it was closed: stop connecting in the background
        if (DataContext is ConnectWindowVM vm)
            vm.CancelTasks();
    }
}