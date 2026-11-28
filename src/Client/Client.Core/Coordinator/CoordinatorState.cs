namespace Client.Core.Coordinator;

public enum CoordinatorState
{
    New = 0,
    Connecting = 1,
    Reconnecting = 2,
    Connected = 3,
    Disconnected = 4,
    Canceled = 5,
    /// <summary>The login needs the password or a one-time code, and the user gave it up.</summary>
    AuthenticationNeeded = 6,
    AuthenticationFailed = 7,
    /// <summary>The user is banned from the coordinator.</summary>
    Denied = 8,
    Rejected = 9,
    CertificateNotTrusted = 10,
    CoordinatorMismatch = 11,
    TimedOut = 12,
    HeartbeatTimedOut = 13,
    HeartbeatServerShutdown = 14,
    InternalServerError = 15,
    Failed = 16,

    /// <summary>
    /// Closed from here while the application is off the screen; restored once it is back.
    /// </summary>
    Suspended = 17,

    /// <summary>The server speaks a newer protocol than this client serves; the application needs an update.</summary>
    ClientOutdated = 18,

    /// <summary>The server speaks an older protocol than this client; the server needs an update.</summary>
    ServerOutdated = 19,

    /// <summary>The account logs in with passkeys alone, which this client cannot use.</summary>
    PasskeyRequired = 20,

    OnlineLimitReached = 21,
    UserLimitReached = 22,

    /// <summary>
    /// The device is not one of the account any more: removed from another device or the control panel, expired, or
    /// its key is gone. The user logs in again another way.
    /// </summary>
    DeviceEnded = 23,

    /// <summary>The account has no password, one-time codes or passkeys, so it cannot be logged in to by username.</summary>
    LoginFactorsMissing = 24,

    /// <summary>The device of the account that was to add this one turned it away, or the code expired.</summary>
    DeviceLinkDeclined = 25,

    /// <summary>The recovery phrase, the login code or the username does not hold.</summary>
    LoginRefused = 26
}
