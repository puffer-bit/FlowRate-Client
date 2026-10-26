using Client.Views.Common;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.ViewModels.Profile;

namespace Client.Views.Profile;

internal partial class AccountSecurityView : ViewControl<AccountSecurityVM>
{
    public AccountSecurityView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            ViewModel!.CloseWindow.RegisterCloseHandler(this).DisposeWith(disposables);
        });
    }
}
