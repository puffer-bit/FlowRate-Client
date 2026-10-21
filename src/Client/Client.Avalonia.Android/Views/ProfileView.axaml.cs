using Avalonia.Controls;
using Avalonia.Threading;

namespace Client.Droid.Views;

/// <summary>
/// The profile of a user on the whole screen. It is also where the user is renamed and gets an avatar.
/// </summary>
public partial class ProfileView : UserControl
{
    public ProfileView()
    {
        InitializeComponent();
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
