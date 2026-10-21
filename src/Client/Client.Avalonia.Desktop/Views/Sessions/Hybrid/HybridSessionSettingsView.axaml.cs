using Client.Views.Common;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.ViewModels.Sessions.Hybrid;

namespace Client.Views.Sessions.Hybrid;

internal partial class HybridSessionSettingsView : ViewControl<HybridSessionSettingsWindowVM>
{
    public HybridSessionSettingsView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            ViewModel!.CloseWindow.RegisterCloseHandler(this).DisposeWith(disposables);

            NameInput.FocusWhenLoaded(selectAll: true);
        });
    }
}
