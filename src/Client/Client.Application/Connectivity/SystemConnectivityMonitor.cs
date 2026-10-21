using System.Net.NetworkInformation;
using Client.Application.Ports;

namespace Client.Application.Connectivity;

/// <summary>
/// Watches the network through the runtime, for the desktop. A sleep of the machine is told by
/// the wall clock running away from a timer: connections do not survive it, and nothing reports it.
/// </summary>
internal sealed class SystemConnectivityMonitor : IConnectivityMonitor, IDisposable
{
    private static readonly TimeSpan ClockCheckInterval = TimeSpan.FromSeconds(5);

    // Longer than any pause a loaded machine gives a timer
    private static readonly TimeSpan SleepThreshold = TimeSpan.FromSeconds(20);

    public bool IsNetworkAvailable => NetworkInterface.GetIsNetworkAvailable();

    public event Action<bool>? Changed;

    private readonly Timer _clockTimer;
    private DateTime _lastClockCheck = DateTime.UtcNow;

    public SystemConnectivityMonitor()
    {
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
        _clockTimer = new Timer(_ => CheckClock(), null, ClockCheckInterval, ClockCheckInterval);
    }

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => Changed?.Invoke(e.IsAvailable);

    private void OnAddressChanged(object? sender, EventArgs e) => Changed?.Invoke(IsNetworkAvailable);

    private void CheckClock()
    {
        var now = DateTime.UtcNow;
        var elapsed = now - _lastClockCheck;
        _lastClockCheck = now;

        // The clock set back by the user gives a negative span, which is not a sleep
        if (elapsed > ClockCheckInterval + SleepThreshold)
            Changed?.Invoke(IsNetworkAvailable);
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        _clockTimer.Dispose();
    }
}
