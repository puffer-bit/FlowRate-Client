namespace Client.ViewModels.Sessions.List;

/// <summary>
/// What the sessions list shows in place of, or as, its items.
/// </summary>
public enum SessionsListState
{
    Offline,
    Reconnecting,
    Loading,
    Failed,
    Loaded
}
