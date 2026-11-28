using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Client.Services.Platform;

public sealed partial class DesktopUserIdleProvider
{
    /// <summary>
    /// The ext-idle-notify-v1 protocol of the Wayland compositor (KWin, wlroots compositors, Hyprland, niri, COSMIC)
    /// over a connection of its own, beside the window on XWayland. The compositor tells when no input came for
    /// <see cref="NotifyAfter"/> and when it comes back, so the idle time is the time since the first minus that
    /// timeout, and zero until then. Version 2 asks for input only, which ignores idle inhibitors such as a playing video.
    /// </summary>
    private sealed unsafe partial class WaylandIdle : IDisposable
    {
        private const string Wayland = "libwayland-client.so.0";
        private const string NotifierName = "ext_idle_notifier_v1";
        private const uint MarshalFlagDestroy = 1;
        private const int PollMs = 500;
        private static readonly TimeSpan NotifyAfter = TimeSpan.FromSeconds(30);

        private readonly ILogger _logger;
        private readonly nint _display;
        private nint _registry;
        private nint _seat;
        private nint _notifier;
        private nint _notification;
        private uint _seatName;
        private uint _notifierName;
        private uint _notifierVersion;
        private GCHandle _self;
        private Thread? _thread;
        private volatile bool _stop;
        private volatile bool _lost;
        private long _idledAt;

        private WaylandIdle(nint display, ILogger logger)
        {
            _display = display;
            _logger = logger;
            _self = GCHandle.Alloc(this);
        }

        /// <summary>Null when there is no Wayland connection or the compositor does not speak the protocol.</summary>
        public static WaylandIdle? TryOpen(ILogger logger)
        {
            WaylandIdle? idle = null;
            try
            {
                var display = wl_display_connect(0);
                if (display == 0)
                    return null;

                idle = new WaylandIdle(display, logger);
                if (!idle.Bind())
                {
                    logger.LogInformation("The Wayland compositor has no {Protocol}; the idle time is not known",
                        NotifierName);
                    idle.Dispose();
                    return null;
                }

                idle._thread = new Thread(idle.Run) { IsBackground = true, Name = "Wayland idle" };
                idle._thread.Start();
                logger.LogInformation("The idle time comes from {Protocol} version {Version}",
                    NotifierName, idle._notifierVersion);
                return idle;
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
                                                  or TypeInitializationException)
            {
                idle?.Dispose();
                logger.LogInformation(exception, "libwayland-client is not available; the idle time is not known");
                return null;
            }
        }

        public TimeSpan? GetIdleTime()
        {
            if (_lost)
                return null;
            var idledAt = Volatile.Read(ref _idledAt);
            return idledAt == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(idledAt) + NotifyAfter;
        }

        private bool Bind()
        {
            var args = stackalloc Argument[4];
            args[0] = default;
            _registry = wl_proxy_marshal_array_flags(_display, 1, Interfaces.Registry,
                wl_proxy_get_version(_display), 0, args);
            if (_registry == 0)
                return false;
            wl_proxy_add_listener(_registry, Interfaces.RegistryListener, GCHandle.ToIntPtr(_self));
            if (wl_display_roundtrip(_display) < 0 || _notifierName == 0 || _seatName == 0)
                return false;

            _seat = BindGlobal(_seatName, Interfaces.Seat, 1);
            _notifier = BindGlobal(_notifierName, Interfaces.Notifier, _notifierVersion);
            if (_seat == 0 || _notifier == 0)
                return false;

            // get_input_idle_notification (2) since version 2, get_idle_notification (1) before
            args[0] = default;
            args[1] = new Argument { Value = (uint)NotifyAfter.TotalMilliseconds };
            args[2] = new Argument { Value = (ulong)_seat };
            _notification = wl_proxy_marshal_array_flags(_notifier, _notifierVersion >= 2 ? 2u : 1u,
                Interfaces.Notification, _notifierVersion, 0, args);
            if (_notification == 0)
                return false;
            wl_proxy_add_listener(_notification, Interfaces.NotificationListener, GCHandle.ToIntPtr(_self));
            return wl_display_roundtrip(_display) >= 0;
        }

        private nint BindGlobal(uint name, nint iface, uint version)
        {
            // wl_registry.bind(name, interface, version, new_id)
            var args = stackalloc Argument[4];
            args[0] = new Argument { Value = name };
            args[1] = new Argument { Value = (ulong)((WlInterface*)iface)->Name };
            args[2] = new Argument { Value = version };
            args[3] = default;
            return wl_proxy_marshal_array_flags(_registry, 0, iface, version, 0, args);
        }

        // Only this thread touches the connection once it runs
        private void Run()
        {
            var fd = wl_display_get_fd(_display);
            try
            {
                while (!_stop)
                {
                    while (wl_display_prepare_read(_display) != 0)
                    {
                        if (wl_display_dispatch_pending(_display) < 0)
                            return;
                    }
                    wl_display_flush(_display);

                    var poll = new PollFd { Fd = fd, Events = PollIn };
                    var ready = Poll(&poll, 1, PollMs);
                    if (ready > 0 && (poll.Revents & PollIn) != 0)
                    {
                        if (wl_display_read_events(_display) < 0 || wl_display_dispatch_pending(_display) < 0)
                            return;
                    }
                    else
                    {
                        wl_display_cancel_read(_display);
                        if (ready > 0)
                            return;
                    }
                }
            }
            finally
            {
                if (!_stop)
                {
                    _lost = true;
                    _logger.LogInformation("The Wayland connection for the idle time was lost");
                }
                Release();
            }
        }

        private void Release()
        {
            var args = stackalloc Argument[1];
            if (_notification != 0)
                wl_proxy_marshal_array_flags(_notification, 0, 0, wl_proxy_get_version(_notification),
                    MarshalFlagDestroy, args);
            if (_notifier != 0)
                wl_proxy_marshal_array_flags(_notifier, 0, 0, wl_proxy_get_version(_notifier),
                    MarshalFlagDestroy, args);
            if (_seat != 0)
                wl_proxy_destroy(_seat);
            if (_registry != 0)
                wl_proxy_destroy(_registry);
            _notification = _notifier = _seat = _registry = 0;
            wl_display_flush(_display);
            wl_display_disconnect(_display);
            if (_self.IsAllocated)
                _self.Free();
        }

        public void Dispose()
        {
            _stop = true;
            if (_thread != null)
                _thread.Join();
            else
                Release();
        }

        private static WaylandIdle? From(nint data) =>
            data == 0 ? null : GCHandle.FromIntPtr(data).Target as WaylandIdle;

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnGlobal(nint data, nint registry, uint name, byte* iface, uint version)
        {
            try
            {
                if (From(data) is not { } idle)
                    return;
                var interfaceName = Marshal.PtrToStringUTF8((nint)iface);
                if (interfaceName == "wl_seat" && idle._seatName == 0)
                    idle._seatName = name;
                else if (interfaceName == NotifierName)
                {
                    idle._notifierName = name;
                    idle._notifierVersion = Math.Min(version, 2u);
                }
            }
            catch
            {
                // Nothing may leave a native callback
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnGlobalRemove(nint data, nint registry, uint name) { }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnIdled(nint data, nint notification)
        {
            try
            {
                if (From(data) is { } idle)
                    Volatile.Write(ref idle._idledAt, Math.Max(1, Stopwatch.GetTimestamp()));
            }
            catch { }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnResumed(nint data, nint notification)
        {
            try
            {
                if (From(data) is { } idle)
                    Volatile.Write(ref idle._idledAt, 0);
            }
            catch { }
        }

        /// <summary>
        /// The wl_interface descriptions of the protocol, made once and never freed: libwayland keeps pointers to them
        /// for as long as a proxy lives. wl_registry and wl_seat come from libwayland-client itself.
        /// </summary>
        private static class Interfaces
        {
            public static readonly nint Registry;
            public static readonly nint Seat;
            public static readonly nint Notifier;
            public static readonly nint Notification;
            public static readonly nint RegistryListener;
            public static readonly nint NotificationListener;

            static Interfaces()
            {
                var library = NativeLibrary.Load(Wayland);
                Registry = NativeLibrary.GetExport(library, "wl_registry_interface");
                Seat = NativeLibrary.GetExport(library, "wl_seat_interface");

                Notification = Alloc<WlInterface>(1);
                Notifier = Alloc<WlInterface>(1);

                // Types of "nuo": the new notification, the timeout, the seat
                var types = (nint*)Alloc<nint>(4);
                types[0] = Notification;
                types[1] = 0;
                types[2] = Seat;
                types[3] = 0;
                var none = types + 3;

                var notifierMethods = (WlMessage*)Alloc<WlMessage>(3);
                notifierMethods[0] = Message("destroy", "", none);
                notifierMethods[1] = Message("get_idle_notification", "nuo", types);
                notifierMethods[2] = Message("get_input_idle_notification", "2nuo", types);
                *(WlInterface*)Notifier = new WlInterface
                {
                    Name = Utf8(NotifierName), Version = 2,
                    MethodCount = 3, Methods = notifierMethods,
                };

                var notificationMethods = (WlMessage*)Alloc<WlMessage>(1);
                notificationMethods[0] = Message("destroy", "", none);
                var notificationEvents = (WlMessage*)Alloc<WlMessage>(2);
                notificationEvents[0] = Message("idled", "", none);
                notificationEvents[1] = Message("resumed", "", none);
                *(WlInterface*)Notification = new WlInterface
                {
                    Name = Utf8("ext_idle_notification_v1"), Version = 2,
                    MethodCount = 1, Methods = notificationMethods,
                    EventCount = 2, Events = notificationEvents,
                };

                var registryListener = (nint*)Alloc<nint>(2);
                registryListener[0] = (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, byte*, uint, void>)&OnGlobal;
                registryListener[1] = (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, void>)&OnGlobalRemove;
                RegistryListener = (nint)registryListener;

                var notificationListener = (nint*)Alloc<nint>(2);
                notificationListener[0] = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnIdled;
                notificationListener[1] = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnResumed;
                NotificationListener = (nint)notificationListener;
            }

            private static nint Alloc<T>(int count) where T : unmanaged =>
                (nint)NativeMemory.AllocZeroed((nuint)count, (nuint)sizeof(T));

            private static byte* Utf8(string value) => (byte*)Marshal.StringToCoTaskMemUTF8(value);

            private static WlMessage Message(string name, string signature, nint* types) =>
                new() { Name = Utf8(name), Signature = Utf8(signature), Types = types };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WlMessage
        {
            public byte* Name;
            public byte* Signature;
            public nint* Types;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WlInterface
        {
            public byte* Name;
            public int Version;
            public int MethodCount;
            public WlMessage* Methods;
            public int EventCount;
            public WlMessage* Events;
        }

        /// <summary>union wl_argument: 8 bytes, numbers in the low ones (little endian).</summary>
        [StructLayout(LayoutKind.Sequential, Size = 8)]
        private struct Argument
        {
            public ulong Value;
        }

        private const short PollIn = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct PollFd
        {
            public int Fd;
            public short Events;
            public short Revents;
        }

        [LibraryImport("libc.so.6", EntryPoint = "poll", SetLastError = true)]
        private static partial int Poll(PollFd* fds, nuint count, int timeoutMs);

        [LibraryImport(Wayland)]
        private static partial nint wl_display_connect(nint name);
        [LibraryImport(Wayland)]
        private static partial void wl_display_disconnect(nint display);
        [LibraryImport(Wayland)]
        private static partial int wl_display_roundtrip(nint display);
        [LibraryImport(Wayland)]
        private static partial int wl_display_get_fd(nint display);
        [LibraryImport(Wayland)]
        private static partial int wl_display_prepare_read(nint display);
        [LibraryImport(Wayland)]
        private static partial int wl_display_read_events(nint display);
        [LibraryImport(Wayland)]
        private static partial void wl_display_cancel_read(nint display);
        [LibraryImport(Wayland)]
        private static partial int wl_display_dispatch_pending(nint display);
        [LibraryImport(Wayland)]
        private static partial int wl_display_flush(nint display);
        [LibraryImport(Wayland)]
        private static partial nint wl_proxy_marshal_array_flags(nint proxy, uint opcode, nint iface, uint version,
            uint flags, Argument* args);
        [LibraryImport(Wayland)]
        private static partial int wl_proxy_add_listener(nint proxy, nint implementation, nint data);
        [LibraryImport(Wayland)]
        private static partial uint wl_proxy_get_version(nint proxy);
        [LibraryImport(Wayland)]
        private static partial void wl_proxy_destroy(nint proxy);
    }
}
