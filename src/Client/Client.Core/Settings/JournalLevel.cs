namespace Client.Core.Settings;

/// <summary>
/// From which level the journal of the application keeps its messages.
/// </summary>
public enum JournalLevel
{
    Verbose = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4
}
