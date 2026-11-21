namespace Client.Api;

/// <summary>
/// What the client tells the server about itself when it logs in; the server shows it in the user's
/// list of sessions. The host names it: the desktop client, the phone or a bot.
/// </summary>
/// <param name="DeviceName">The machine or the phone, e.g. "DESKTOP-1" or "Google Pixel 9".</param>
/// <param name="Platform">The operating system: "Windows", "Linux", "macOS" or "Android".</param>
/// <param name="AppVersion">The version of the application that logs in, e.g. "0.7.1".</param>
public sealed record ClientInfo(string DeviceName, string Platform, string AppVersion);
