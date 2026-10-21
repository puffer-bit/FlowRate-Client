using Shared.Enums;

namespace Client.Application.Ports;

/// <summary>
/// The address of this device in the push service of its platform. Registered only on platforms
/// that have one.
/// </summary>
public interface IPushTokenProvider
{
    PushPlatform Platform { get; }

    /// <summary>
    /// Raised when the push service replaced the token. Raised on a thread of the push service.
    /// </summary>
    event Action<string>? TokenChanged;

    /// <returns>Null when the push service is not available on the device.</returns>
    Task<string?> GetTokenAsync();
}
