using ReactiveUI;
using Client.Views.Common;
using ReactiveUI.Primitives;
using Client.Views.Dialogs;
using Client.ViewModels.Sessions.List;

namespace Client.Views.Sessions.List;

public partial class SessionListChatItemView : ViewControl<ChatSessionVM>
{
    public SessionListChatItemView()
    {
        InitializeComponent();
        
        this.WhenActivated(disposables =>
        {
            ViewModel!.ShowMessageBox.RegisterMessageBoxHandler(this).DisposeWith(disposables);
        });
    }
}