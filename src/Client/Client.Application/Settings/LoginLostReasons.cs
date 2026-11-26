using Client.Core.Settings;
using Shared.Enums.Auth;

namespace Client.Application.Settings;

public static class LoginLostReasons
{
    /// <summary>The reason a device ended as the server told it; none means the server did not know the device.</summary>
    public static LoginLostReason From(DeviceEndReason? reason) => reason switch
    {
        DeviceEndReason.Revoked => LoginLostReason.Revoked,
        DeviceEndReason.LoggedOut => LoginLostReason.LoggedOut,
        _ => LoginLostReason.NotFound
    };
}
