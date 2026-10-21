namespace Client.Tools.AppInfo;

public static class AppDirectories
{
    /// <summary>
    /// The folder of everything the client keeps between runs: the database, the logs, the caches and
    /// the sound packs. It is in the profile of the user, "%LocalAppData%\FlowRate" on Windows and
    /// "~/.local/share/FlowRate" on Linux, not next to the executable: an update replaces the folder
    /// of the application as a whole, and on Linux that folder is a read-only AppImage.
    /// </summary>
    public static string Data { get; } = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData,
            System.Environment.SpecialFolderOption.DoNotVerify),
        "FlowRate");
}
