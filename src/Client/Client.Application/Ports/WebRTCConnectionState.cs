namespace Client.Application.Ports;

/// <summary>How the connection of a WebRTC transport is doing.</summary>
public enum WebRTCConnectionState
{
    New = 0,
    Connecting = 1,
    Connected = 2,
    Disconnected = 3,
    Failed = 4,
    Closed = 5,
    Fatal = 6
}

/// <summary>How the ICE connection of a WebRTC transport is doing.</summary>
public enum WebRTCICEConnectionState
{
    New = 0,
    Checking = 1,
    Connected = 2,
    Completed = 3,
    Failed = 4,
    Disconnected = 5,
    Closed = 6
}
