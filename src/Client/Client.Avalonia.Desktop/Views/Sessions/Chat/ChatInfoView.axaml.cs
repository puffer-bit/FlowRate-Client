using Client.ViewModels.Sessions.Chat;
using Client.Views.Common;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace Client.Views.Sessions.Chat;

internal partial class ChatInfoView : ViewControl<ChatInfoWindowVM>
{
    public ChatInfoView()
    {
        InitializeComponent();
        this.WhenActivated(disposables =>
            ViewModel!.CloseWindow.RegisterCloseHandler(this).DisposeWith(disposables));
    }
}
