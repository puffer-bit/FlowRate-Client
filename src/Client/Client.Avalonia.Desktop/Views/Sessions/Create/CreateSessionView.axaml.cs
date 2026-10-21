using Client.Views.Common;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.ViewModels.Sessions.Create;

namespace Client.Views.Sessions.Create;

internal partial class CreateSessionView : ViewControl<CreateSessionWindowVM>
{
    public CreateSessionView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            ViewModel!.CloseWindow.RegisterCloseHandler(this).DisposeWith(disposables);

            // The dialog opens straight into typing the name
            NameInput.FocusWhenLoaded();
        });
    }
}
