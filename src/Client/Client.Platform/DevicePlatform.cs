namespace Client.Platform;

/// <summary>
/// The operating system the client runs on, as the server shows it in the user's list of sessions
/// and the journal names it.
/// </summary>
public static class DevicePlatform
{
    public static string Name =>
        OperatingSystem.IsAndroid() ? "Android"
        : OperatingSystem.IsWindows() ? "Windows"
        : OperatingSystem.IsLinux() ? "Linux"
        : OperatingSystem.IsMacOS() ? "macOS"
        : "Unknown";
}
