using ReactiveUI;
using Client.Views.Common;
using ReactiveUI.Primitives;
using Client.Views.Dialogs;
using Client.ViewModels.Settings.Pages;

namespace Client.Views.Settings.Pages;

public partial class AccountingControl : ViewControl<AccountingControlVM>
{
    public AccountingControl()
    {
        InitializeComponent();
        
        this.WhenActivated(disposables =>
        {
            ViewModel!.ShowMessageBox.RegisterMessageBoxHandler(this).DisposeWith(disposables);
            ViewModel!.AskDatabasePassword.RegisterDatabasePasswordHandler(this).DisposeWith(disposables);
        });
    }
}
