namespace Client.Core.Settings;

/// <summary>Why a saved account lost its device here and has to log in again. Stored by number.</summary>
public enum LoginLostReason
{
    /// <summary>The server no longer knows the device: it expired or was removed while this device was away.</summary>
    NotFound = 0,

    /// <summary>Another device of the account, or the control panel, removed it.</summary>
    Revoked = 1,

    /// <summary>The user logged out on this device.</summary>
    LoggedOut = 2
}
