using Avalonia.Controls;
using Client.Views.Common;
using Client.ViewModels.Connections;

namespace Client.Views.Dialogs;

/// <summary>
/// Asks for what finishes a login, see <see cref="LoginPromptVM"/>. Shown with <see cref="LoginPromptInteraction"/>.
/// </summary>
internal partial class LoginPromptDialog : UserControl
{
    public LoginPromptDialog()
    {
        InitializeComponent();

        // The password comes first when both are asked
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is LoginPromptVM { IsPasswordAsked: true })
                PasswordInput.FocusWhenLoaded();
            else
                CodeInput.FocusWhenLoaded();
        };
    }
}
