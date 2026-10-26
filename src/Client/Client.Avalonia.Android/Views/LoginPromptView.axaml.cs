using Avalonia.Controls;

namespace Client.Droid.Views;

/// <summary>
/// Asks for the account password or a one-time code that finishes a login, over whatever is on the screen.
/// </summary>
public partial class LoginPromptView : UserControl
{
    public LoginPromptView()
    {
        InitializeComponent();
    }
}
