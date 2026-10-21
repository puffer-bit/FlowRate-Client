namespace Client.Core.Settings;

/// <summary>What the close button of the main window does.</summary>
public enum CloseButtonAction
{
    /// <summary>Asks on the next close and remembers the answer.</summary>
    Ask = 0,
    MinimizeToTray = 1,
    Exit = 2,
}
