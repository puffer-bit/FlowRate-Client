using Avalonia.Interactivity;
using Avalonia.Threading;
using Client.Views.Dialogs;
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

        // The note sets its text before it is ever focused, and its caret would pull the profile down to it;
        // the profile opens from its top
        NoteInput.AddHandler(RequestBringIntoViewEvent, (_, e) =>
        {
            if (!NoteInput.IsKeyboardFocusWithin)
                e.Handled = true;
        }, RoutingStrategies.Bubble);
        Loaded += (_, _) => Dispatcher.UIThread.Post(ContentScroll.ScrollToHome, DispatcherPriority.Background);

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
