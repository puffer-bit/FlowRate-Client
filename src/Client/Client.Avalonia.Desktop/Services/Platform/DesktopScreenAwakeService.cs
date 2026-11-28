using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Client.Application.Settings;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace Client.Services.Platform;

/// <summary>Native display sleep inhibition; all state changes originate on the UI thread.</summary>
public sealed partial class DesktopScreenAwakeService : ScreenAwakeService
{
    private const uint Continuous = 0x80000000;
    private const uint DisplayRequired = 0x00000002;
    private const uint SystemRequired = 0x00000001;
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private readonly ILogger<DesktopScreenAwakeService> _logger;
    private uint? _assertion;
    private LinuxInhibition? _linux;

    public DesktopScreenAwakeService(IBehaviorService behavior, ILogger<DesktopScreenAwakeService> logger)
        : base(behavior, logger) => _logger = logger;

    protected override void SetRequired(bool required)
    {
        if (OperatingSystem.IsWindows())
        {
            if (SetThreadExecutionState(Continuous | (required ? DisplayRequired | SystemRequired : 0)) == 0)
                throw new InvalidOperationException("SetThreadExecutionState failed");
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (required)
            {
                var type = CFStringCreateWithCString(0, "PreventUserIdleDisplaySleep", 0x08000100);
                var reason = CFStringCreateWithCString(0, "Watching a FlowRate stream", 0x08000100);
                try
                {
                    if (type == 0 || reason == 0)
                        throw new InvalidOperationException("Could not create power assertion strings");
                    var result = IOPMAssertionCreateWithName(type, 255, reason, out var assertion);
                    if (result != 0) throw new InvalidOperationException($"IOPMAssertionCreateWithName: {result}");
                    _assertion = assertion;
                }
                finally
                {
                    if (type != 0) CFRelease(type);
                    if (reason != 0) CFRelease(reason);
                }
            }
            else if (_assertion is { } assertion)
            {
                _assertion = null;
                var result = IOPMAssertionRelease(assertion);
                if (result != 0) throw new InvalidOperationException($"IOPMAssertionRelease: {result}");
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            if (required)
            {
                _linux = new LinuxInhibition(_logger);
                _linux.Start();
            }
            else
            {
                _linux?.Dispose();
                _linux = null;
            }
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint flags);
    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CFStringCreateWithCString(nint allocator, string text, uint encoding);
    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint value);
    [LibraryImport(IOKit)]
    private static partial int IOPMAssertionCreateWithName(nint type, uint level, nint name, out uint assertion);
    [LibraryImport(IOKit)]
    private static partial int IOPMAssertionRelease(uint assertion);

    private sealed class LinuxInhibition(ILogger logger) : IDisposable
    {
        private const string Service = "org.freedesktop.portal.Desktop";
        private DBusConnection? _connection;
        private string? _handle;
        private readonly CancellationTokenSource _stop = new();

        public void Start() => _ = StartAsync();

        private async Task StartAsync()
        {
            var cancellation = _stop.Token;
            DBusConnection? connection = null;
            try
            {
                connection = new DBusConnection(DBusAddress.Session
                    ?? throw new InvalidOperationException("No D-Bus session bus"));
                _connection = connection;
                await connection.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8), cancellation);
                var handle = await connection.CallMethodAsync(CreateInhibit(connection),
                    static (Message message, object? _) => message.GetBodyReader().ReadObjectPathAsString())
                    .WaitAsync(TimeSpan.FromSeconds(8), cancellation);
                // Disposal can race either await. A stale request never becomes the active lease.
                if (cancellation.IsCancellationRequested) return;
                _handle = handle;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (!cancellation.IsCancellationRequested)
                    logger.LogWarning(exception, "Screen awake inhibition is unavailable through the desktop portal");
                connection?.Dispose();
            }
        }

        private static MessageBuffer CreateInhibit(DBusConnection connection)
        {
            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(Service, "/org/freedesktop/portal/desktop",
                "org.freedesktop.portal.Inhibit", "Inhibit", "sua{sv}");
            writer.WriteString("");
            writer.WriteUInt32(8); // Idle: inhibit display sleep, without blocking logout or user switching.
            var options = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteString("reason");
            writer.WriteVariantString("Watching a FlowRate stream");
            writer.WriteDictionaryEnd(options);
            return writer.CreateMessage();
        }

        public void Dispose()
        {
            _stop.Cancel();
            _stop.Dispose();
            var connection = _connection;
            _connection = null;
            if (connection == null) return;
            if (_handle is { } handle) _ = CloseAsync(connection, handle);
            // Disconnecting also removes inhibition, including a still-pending Inhibit call.
            else connection.Dispose();
        }

        private static async Task CloseAsync(DBusConnection connection, string handle)
        {
            try
            {
                await connection.CallMethodAsync(CreateClose(connection, handle)).WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception) { /* Disconnecting releases the request if Close is unavailable. */ }
            finally { connection.Dispose(); }
        }

        private static MessageBuffer CreateClose(DBusConnection connection, string handle)
        {
            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(Service, handle, "org.freedesktop.portal.Request", "Close");
            return writer.CreateMessage();
        }
    }
}
