using Client.Views.Common;
using Client.Views.Dialogs;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.ViewModels.ServerSettings;

namespace Client.Views.ServerSettings;

internal partial class ServerSettingsView : ViewControl<ServerSettingsVM>
{
    public ServerSettingsView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            ViewModel!.CloseWindow.RegisterCloseHandler(this).DisposeWith(disposables);
            ViewModel!.ShowMessageBox.RegisterMessageBoxHandler(this).DisposeWith(disposables);
        });
    }
}
