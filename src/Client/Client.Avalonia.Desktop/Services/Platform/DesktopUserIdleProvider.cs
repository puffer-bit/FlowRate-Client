using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Client.Application.Ports;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace Client.Services.Platform;

/// <summary>
/// The time since the last input in the whole system: GetLastInputInfo on Windows, the HID event source on macOS,
/// the XScreenSaver extension on X11 and the Mutter idle monitor on GNOME Wayland. Elsewhere, such as other Wayland
/// compositors, the idle time is not known and the status never turns idle.
/// </summary>
public sealed partial class DesktopUserIdleProvider : IUserIdleProvider, IDisposable
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const int HIDSystemState = 1;
    private const uint AnyInputEventType = uint.MaxValue;

    private readonly ILogger<DesktopUserIdleProvider> _logger;
    private readonly X11Idle? _x11;
    private readonly MutterIdle? _mutter;
    private bool _failed;

    public DesktopUserIdleProvider(ILogger<DesktopUserIdleProvider> logger)
    {
        _logger = logger;
        if (!OperatingSystem.IsLinux())
            return;

        // XWayland sees only the input of X clients, so a Wayland session asks the compositor
        if (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is { Length: > 0 })
            _mutter = new MutterIdle(logger);
        else if (Environment.GetEnvironmentVariable("DISPLAY") is { Length: > 0 })
            _x11 = X11Idle.TryOpen(logger);
    }

    public TimeSpan? GetIdleTime()
    {
        if (_failed)
            return null;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
                if (!GetLastInputInfo(ref info))
                    return null;
                // Both wrap around after 49.7 days; the difference does not
                return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
            }

            if (OperatingSystem.IsMacOS())
                return TimeSpan.FromSeconds(CGEventSourceSecondsSinceLastEventType(HIDSystemState, AnyInputEventType));

            if (OperatingSystem.IsLinux())
                return _x11?.GetIdleTime() ?? _mutter?.GetIdleTime();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            _failed = true;
            _logger.LogInformation(exception, "The idle time of the system is not available");
        }

        return null;
    }

    public void Dispose()
    {
        _x11?.Dispose();
        _mutter?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo info);

    [LibraryImport(CoreGraphics)]
    private static partial double CGEventSourceSecondsSinceLastEventType(int stateId, uint eventType);

    /// <summary>The XScreenSaver extension of the X server, which counts the input of every X client.</summary>
    private sealed partial class X11Idle : IDisposable
    {
        private const string X11 = "libX11.so.6";
        private const string Xss = "libXss.so.1";

        // XScreenSaverInfo: Window window, int state, int kind, unsigned long til_or_since, unsigned long idle, ...
        private static readonly int IdleOffset = IntPtr.Size * 2 + sizeof(int) * 2;

        private nint _display;
        private readonly nint _root;
        private readonly nint _info;

        private X11Idle(nint display)
        {
            _display = display;
            _root = XDefaultRootWindow(display);
            _info = XScreenSaverAllocInfo();
        }

        public static X11Idle? TryOpen(ILogger logger)
        {
            try
            {
                var display = XOpenDisplay(0);
                if (display == 0)
                    return null;
                if (XScreenSaverQueryExtension(display, out _, out _) != 0)
                    return new X11Idle(display);
                XCloseDisplay(display);
                logger.LogInformation("The X server has no XScreenSaver extension; the idle time is not known");
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
            {
                logger.LogInformation(exception, "libXss is not available; the idle time is not known");
            }
            return null;
        }

        public TimeSpan? GetIdleTime()
        {
            if (_display == 0 || _info == 0 || XScreenSaverQueryInfo(_display, _root, _info) == 0)
                return null;
            return TimeSpan.FromMilliseconds((ulong)Marshal.ReadIntPtr(_info, IdleOffset));
        }

        public void Dispose()
        {
            if (_info != 0)
                XFree(_info);
            if (_display != 0)
                XCloseDisplay(_display);
            _display = 0;
        }

        [LibraryImport(X11)]
        private static partial nint XOpenDisplay(nint name);
        [LibraryImport(X11)]
        private static partial int XCloseDisplay(nint display);
        [LibraryImport(X11)]
        private static partial nint XDefaultRootWindow(nint display);
        [LibraryImport(X11)]
        private static partial int XFree(nint data);
        [LibraryImport(Xss)]
        private static partial int XScreenSaverQueryExtension(nint display, out int eventBase, out int errorBase);
        [LibraryImport(Xss)]
        private static partial nint XScreenSaverAllocInfo();
        [LibraryImport(Xss)]
        private static partial int XScreenSaverQueryInfo(nint display, nint drawable, nint info);
    }

    /// <summary>
    /// The idle monitor of GNOME Shell on the session bus. Asked in the background: a check gets the answer to the
    /// one before, a few seconds old, which is close enough for minutes.
    /// </summary>
    private sealed class MutterIdle(ILogger logger) : IDisposable
    {
        private const string Service = "org.gnome.Mutter.IdleMonitor";

        private readonly CancellationTokenSource _stop = new();
        private DBusConnection? _connection;
        private TimeSpan? _last;
        private int _asking;
        private bool _unavailable;

        public TimeSpan? GetIdleTime()
        {
            if (!_unavailable && Interlocked.Exchange(ref _asking, 1) == 0)
                _ = AskAsync();
            return _last;
        }

        private async Task AskAsync()
        {
            var cancellation = _stop.Token;
            try
            {
                if (_connection == null)
                {
                    var connection = new DBusConnection(DBusAddress.Session
                        ?? throw new InvalidOperationException("No D-Bus session bus"));
                    await connection.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8), cancellation);
                    _connection = connection;
                }

                var idle = await _connection.CallMethodAsync(CreateGetIdletime(_connection),
                        static (Message message, object? _) => message.GetBodyReader().ReadUInt64())
                    .WaitAsync(TimeSpan.FromSeconds(4), cancellation);
                _last = TimeSpan.FromMilliseconds(idle);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception exception)
            {
                // Not GNOME, or a GNOME without the monitor: asked no more
                _unavailable = true;
                _last = null;
                logger.LogInformation(exception, "The Mutter idle monitor is not available; the idle time is not known");
            }
            finally
            {
                Volatile.Write(ref _asking, 0);
            }
        }

        private static MessageBuffer CreateGetIdletime(DBusConnection connection)
        {
            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(Service, "/org/gnome/Mutter/IdleMonitor/Core",
                "org.gnome.Mutter.IdleMonitor", "GetIdletime");
            return writer.CreateMessage();
        }

        public void Dispose()
        {
            _stop.Cancel();
            _stop.Dispose();
            _connection?.Dispose();
            _connection = null;
        }
    }
}
