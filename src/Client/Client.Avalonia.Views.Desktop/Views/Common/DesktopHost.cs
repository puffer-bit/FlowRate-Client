namespace Client.Views.Common;

/// <summary>What the desktop views ask of the application that hosts them.</summary>
public interface IDesktopHost
{
    /// <summary>Starts the application again, to apply what is read only at the start.</summary>
    void Restart();

    /// <summary>Starts the application again without voice and video, after the media runtime failed.</summary>
    void RestartWithoutMedia();
}

/// <summary>The host of the desktop views, set by it before the first view is shown.</summary>
public static class DesktopHost
{
    public static IDesktopHost? Current { get; set; }
}

/// <summary>
/// The main window, which may hide to the tray or ask before it closes instead of closing at once.
/// The window buttons drawn by the views close it through this.
/// </summary>
public interface IMainWindow
{
    void RequestClose();
}
