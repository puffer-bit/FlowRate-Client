using System;
using Android.Content;
using Android.Net;
using Client.Application.Ports;

namespace Client.Droid.Platform.Network;

/// <summary>
/// Follows the network the system routes the application through: Wi-Fi replaced by mobile data
/// is another network, and the sockets of the old one are dead.
/// </summary>
internal sealed class AndroidConnectivityMonitor : IConnectivityMonitor
{
    public bool IsNetworkAvailable => _manager?.ActiveNetwork != null;

    public event Action<bool>? Changed;

    private readonly ConnectivityManager? _manager;

    // Kept for as long as the process lives: the monitor is a singleton
    private readonly DefaultNetworkCallback? _callback;

    public AndroidConnectivityMonitor()
    {
        _manager = global::Android.App.Application.Context.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
        if (_manager == null)
            return;

        _callback = new DefaultNetworkCallback(this);
        _manager.RegisterDefaultNetworkCallback(_callback);
    }

    private sealed class DefaultNetworkCallback(AndroidConnectivityMonitor owner) : ConnectivityManager.NetworkCallback
    {
        // Also called for the network that replaces another, with no loss in between
        public override void OnAvailable(global::Android.Net.Network network) => owner.Changed?.Invoke(true);

        public override void OnLost(global::Android.Net.Network network) => owner.Changed?.Invoke(false);
    }
}
