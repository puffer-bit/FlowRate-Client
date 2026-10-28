namespace Client.Tools.AppInfo;

/// <summary>What the client tells the server about the device it logs in from, shown in the user's list of sessions.</summary>
public static class DeviceInformation
{
    /// <summary>The name of the machine; Android has none worth showing, and sets the model of the phone instead.</summary>
    public static string Name { get; set; } = System.Environment.MachineName;

    public static string Platform =>
        OperatingSystem.IsAndroid() ? "Android"
        : OperatingSystem.IsWindows() ? "Windows"
        : OperatingSystem.IsLinux() ? "Linux"
        : OperatingSystem.IsMacOS() ? "macOS"
        : "Unknown";
}
