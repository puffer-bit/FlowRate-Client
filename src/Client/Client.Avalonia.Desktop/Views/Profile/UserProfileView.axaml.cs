using Client.Views.Common;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.ViewModels.Profile;

namespace Client.Views.Profile;

internal partial class UserProfileView : ViewControl<UserProfileWindowVM>
{
    public UserProfileView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            ViewModel!.CloseWindow.RegisterCloseHandler(this).DisposeWith(disposables);
            AvatarFilePicker.Register(ViewModel!.AvatarPicker, this).DisposeWith(disposables);

            // The field only becomes visible with this change, so it is focused after the layout pass
            ViewModel!.FocusName.RegisterHandler(interaction =>
            {
                NameInput.FocusWhenLoaded(selectAll: true);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);
        });
    }
}
