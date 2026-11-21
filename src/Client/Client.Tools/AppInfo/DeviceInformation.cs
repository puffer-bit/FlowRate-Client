namespace Client.Tools.AppInfo;

/// <summary>The device the client runs on, as the journal names it and the host tells the server; the platform is <see cref="Client.Platform.DevicePlatform"/>.</summary>
public static class DeviceInformation
{
    /// <summary>The name of the machine; Android has none worth showing, and sets the model of the phone instead.</summary>
    public static string Name { get; set; } = System.Environment.MachineName;
}
