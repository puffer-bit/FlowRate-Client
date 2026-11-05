namespace Client.ViewModels.ServerSettings;

/// <summary>
/// What the server password becomes on saving; the index of the choice in the combo box of the view.
/// </summary>
public enum PasswordChange
{
    Keep = 0,
    Set = 1,
    Remove = 2
}
