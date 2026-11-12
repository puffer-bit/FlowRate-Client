namespace Client.ViewModels.Main;

public enum UpdaterState
{
    /// <summary>Nothing has been asked yet.</summary>
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    /// <summary>Downloaded: a restart applies it, and so does the next start of the application.</summary>
    ReadyToRestart,
    Failed
}
