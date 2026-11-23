namespace Client.Api;

/// <summary>
/// What the client tells the server about itself when it logs in; the server shows it in the user's
/// list of sessions. The host names it: the desktop client, the phone or a bot.
/// </summary>
/// <param name="DeviceName">The machine or the phone, e.g. "DESKTOP-1" or "Google Pixel 9".</param>
/// <param name="Platform">The operating system: "Windows", "Linux", "macOS" or "Android".</param>
/// <param name="AppVersion">The version of the application that logs in, e.g. "0.7.1".</param>
/// <param name="OsVersion">The system and its version, e.g. "Windows 11 (26100)" or "Android 16 (API 36)".</param>
/// <param name="Architecture">The processor architecture of the system, e.g. "x64" or "arm64".</param>
public sealed record ClientInfo(string DeviceName, string Platform, string AppVersion,
    string? OsVersion = null, string? Architecture = null);
