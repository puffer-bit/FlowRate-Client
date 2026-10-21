namespace Client.Core.Sessions.Hybrid;

public enum HybridSessionState
{
    New = 0,
    WaitingForConnection = 1,
    WaitingForNegotiation = 2,
    Connected = 3,
    Reconnecting = 4,
    Disconnected = 5,
    Canceled = 6,
    Failed = 7,
    TimedOut = 8,
    Deleted = 9,

    /// <summary>The server has no SFU node for the session; we rejoin once it gets one.</summary>
    WaitingForSfuNode = 10
}
