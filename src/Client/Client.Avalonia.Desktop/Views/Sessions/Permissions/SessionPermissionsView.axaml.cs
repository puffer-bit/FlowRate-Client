using Client.Views.Common;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.ViewModels.Sessions.Permissions;

namespace Client.Views.Sessions.Permissions;

internal partial class SessionPermissionsView : ViewControl<SessionPermissionsWindowVM>
{
    public SessionPermissionsView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            ViewModel!.CloseWindow.RegisterCloseHandler(this).DisposeWith(disposables);
        });
    }
}
