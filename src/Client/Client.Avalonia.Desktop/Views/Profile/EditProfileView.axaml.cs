using Client.Views.Common;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.ViewModels.Profile;

namespace Client.Views.Profile;

internal partial class EditProfileView : ViewControl<EditProfileWindowVM>
{
    public EditProfileView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            ViewModel!.CloseWindow.RegisterCloseHandler(this).DisposeWith(disposables);
            AvatarFilePicker.Register(ViewModel!.AvatarPicker, this).DisposeWith(disposables);

            NameInput.FocusWhenLoaded(selectAll: true);
        });
    }
}
