namespace Client.Application.Ports;

/// <summary>
/// Tells when the network of the device changes, so that a connection is checked or restored at
/// once instead of on the next heartbeat or retry.
/// </summary>
public interface IConnectivityMonitor
{
    /// <summary>
    /// Whether the device has a network to connect through. A hint only: a server may still be
    /// reachable when this is false, and unreachable when it is true.
    /// </summary>
    bool IsNetworkAvailable { get; }

    /// <summary>
    /// Raised with <see cref="IsNetworkAvailable"/> when the network came, went or was replaced by
    /// another, and when the device woke up from sleep. Raised on a thread of the monitor.
    /// </summary>
    event Action<bool>? Changed;
}
