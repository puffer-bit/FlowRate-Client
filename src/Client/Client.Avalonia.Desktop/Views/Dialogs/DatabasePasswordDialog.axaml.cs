using Avalonia.Controls;

namespace Client.Views.Dialogs;

/// <summary>
/// Asks for the password of the database, see <see cref="ViewModels.Dialogs.DatabasePasswordVM"/>.
/// Shown with <see cref="DatabasePasswordInteraction"/>.
/// </summary>
internal partial class DatabasePasswordDialog : UserControl
{
    public DatabasePasswordDialog()
    {
        InitializeComponent();
    }
}
