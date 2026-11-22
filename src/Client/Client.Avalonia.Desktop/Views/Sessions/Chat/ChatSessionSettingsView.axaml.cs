using Client.Views.Dialogs;
using Client.Views.Common;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.ViewModels.Sessions.Chat;

namespace Client.Views.Sessions.Chat;

internal partial class ChatSessionSettingsView : ViewControl<ChatSessionSettingsWindowVM>
{
    public ChatSessionSettingsView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            ViewModel!.CloseWindow.RegisterCloseHandler(this).DisposeWith(disposables);

            NameInput.FocusWhenLoaded(selectAll: true);
        });
    }
}
