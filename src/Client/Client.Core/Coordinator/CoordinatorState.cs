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
    PasskeyRequired = 20
}
