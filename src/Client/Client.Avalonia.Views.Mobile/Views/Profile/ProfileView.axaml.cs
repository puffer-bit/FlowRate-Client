using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Client.Mobile.Views.Profile;

/// <summary>
/// The profile of a user on the whole screen. It is also where the user is renamed and gets an avatar.
/// </summary>
public partial class ProfileView : UserControl
{
    public ProfileView()
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
    }

    /// <summary>
    /// Moves the focus into the name field with the name selected, so a new one replaces it.
    /// </summary>
    public void FocusNameInput()
    {
        // The field only becomes visible with the change that asks for this, so it is focused after the layout pass
        Dispatcher.UIThread.Post(() =>
        {
            NameInput.Focus();
            NameInput.SelectAll();
        }, DispatcherPriority.Background);
    }
}
