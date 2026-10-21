using ReactiveUI.Primitives;
using Client.ViewModels.Settings.Pages;
using Client.Views.Common;
using Client.Views.Dialogs;
using ReactiveUI;

namespace Client.Views.Settings.Pages;

public partial class StorageControl : ViewControl<StorageControlVM>
{
    public StorageControl()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            ViewModel!.ShowMessageBox.RegisterMessageBoxHandler(this).DisposeWith(disposables);
        });
    }
}
