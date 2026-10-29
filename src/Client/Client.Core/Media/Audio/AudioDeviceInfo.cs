namespace Client.Core.Media;

/// <param name="Id">Stable identifier that survives reboots and replugging, used to persist the choice.</param>
/// <param name="Name">Human readable name shown in the UI.</param>
/// <param name="IsDefault">Whether the system currently uses this device as the default one.</param>
public record AudioDeviceInfo(string Id, string Name, bool IsDefault);
