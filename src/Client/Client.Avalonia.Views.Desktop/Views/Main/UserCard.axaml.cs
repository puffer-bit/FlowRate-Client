using ReactiveUI;
using Client.Views.Common;
using ReactiveUI.Primitives;
using Client.ViewModels.Main;

namespace Client.Views.Main;

public partial class UserCard : ViewControl<UserCardVM>
{
    public UserCard()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            // The menu would stay on top of the profile dialog it opened
            ViewModel!.EditProfileCommand.IsExecuting
                .Where(isExecuting => isExecuting)
                .Subscribe(_ => ContextFlyout?.Hide())
                .DisposeWith(disposables);
        });
    }
}
