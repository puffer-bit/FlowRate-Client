using Shared.Models.DataTransferObjects;

namespace Client.Core.Users;

/// <summary>A device a user is connected from, as the server tells others.</summary>
/// <param name="Platform">"Windows", "Linux", "macOS", "Android"...</param>
/// <param name="OsVersion">The system and its version; null when that client did not tell.</param>
/// <param name="Architecture">"x64", "arm64"...; null when that client did not tell.</param>
/// <param name="AppVersion">The version of FlowRate on it.</param>
public sealed record UserDeviceModel(string Platform, string? OsVersion, string? Architecture, string AppVersion)
{
    public bool IsMobile => DevicePlatforms.IsMobile(Platform);
}
