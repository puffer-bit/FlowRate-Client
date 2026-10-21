using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Client.GStreamer.Interop;

/// <summary>
/// Watches one property of an object through its notify signal, without a wrapper of the binding.
/// </summary>
/// <remarks>
/// For objects that are better left unwrapped: the transports of webrtcbin are floating, and a
/// reference left on one keeps the ICE agent, with its sockets, after the session is left. The
/// watch holds exactly the one reference it was given.
/// </remarks>
internal sealed unsafe class GstPropertyWatch : IDisposable
{
    [DllImport(NativeLibraries.GObject)]
    private static extern CULong g_signal_connect_data(IntPtr instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string signal,
        delegate* unmanaged<IntPtr, IntPtr, IntPtr, void> handler, IntPtr data, IntPtr destroyData, int flags);

    [DllImport(NativeLibraries.GObject)]
    private static extern void g_signal_handler_disconnect(IntPtr instance, CULong handler);

    // The callback finds its watch by a key rather than by a pointer to it: a notification that
    // is already running when the watch is disposed then finds nothing instead of freed memory
    private static readonly ConcurrentDictionary<IntPtr, GstPropertyWatch> Watches = new();
    private static long _lastKey;

    private readonly IntPtr _instance;
    private readonly IntPtr _key;
    private readonly Action<IntPtr> _onChanged;
    private readonly CULong _handler;
    private int _isDisposed;

    /// <param name="instance">A reference the watch takes over and gives back when disposed.</param>
    /// <param name="onChanged">
    /// Called with the object on the thread that changed the property. It must not throw.
    /// </param>
    public GstPropertyWatch(IntPtr instance, string property, Action<IntPtr> onChanged)
    {
        _instance = instance;
        _onChanged = onChanged;
        _key = new IntPtr(Interlocked.Increment(ref _lastKey));

        Watches[_key] = this;
        _handler = g_signal_connect_data(instance, $"notify::{property}", &OnNotify, _key, IntPtr.Zero, 0);
    }

    [UnmanagedCallersOnly]
    private static void OnNotify(IntPtr instance, IntPtr parameter, IntPtr key)
    {
        try
        {
            if (Watches.TryGetValue(key, out var watch))
                watch._onChanged(instance);
        }
        catch
        {
            // An exception must not cross into native code
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
            return;

        Watches.TryRemove(_key, out _);
        g_signal_handler_disconnect(_instance, _handler);
        GstNative.Release(_instance);
    }
}
