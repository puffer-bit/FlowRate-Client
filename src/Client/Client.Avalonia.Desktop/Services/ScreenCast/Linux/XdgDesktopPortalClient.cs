using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Client.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;

namespace Client.Services.ScreenCast.Linux;

/// <summary>
/// Screen cast interface of xdg-desktop-portal, which is how a Wayland session lets an application
/// see the screen. It works the same way on X11 desktops that run a portal.
/// </summary>
/// <remarks>
/// The messages are written by hand on top of Tmds.DBus.Protocol: its proxy-generating sibling
/// Tmds.DBus emits code at run time, which NativeAOT cannot do.
/// </remarks>
public sealed partial class XdgDesktopPortalClient : IScreenCastClient
{
    private const string Service = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";
    private const string ScreenCastInterface = "org.freedesktop.portal.ScreenCast";
    private const string RequestInterface = "org.freedesktop.portal.Request";
    private const string SessionInterface = "org.freedesktop.portal.Session";

    private const uint SourceTypeMonitor = 1;
    private const uint SourceTypeWindow = 2;
    private const uint CursorModeEmbedded = 2;

    private const uint ResponseSuccess = 0;
    private const uint ResponseCancelled = 1;

    // The dialog stays open until the user decides
    private static readonly TimeSpan InteractiveTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(25);

    private readonly ILogger<XdgDesktopPortalClient> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private DBusConnection? _connection;
    private string? _sessionPath;
    private IDisposable? _sessionWatch;

    public event Action? Closed;
    private bool _disposed;

    public XdgDesktopPortalClient(ILogger<XdgDesktopPortalClient> logger)
    {
        _logger = logger;
    }

    public async Task<VideoInput?> SelectSourceAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            await CloseSessionAsync();

            var connection = await ConnectAsync();

            var created = await RequestAsync(connection, "CreateSession", "a{sv}", CallTimeout,
                static (ref MessageWriter writer, string token, string? _) =>
                {
                    var options = writer.WriteDictionaryStart();
                    WriteOption(ref writer, "handle_token", token);
                    WriteOption(ref writer, "session_handle_token", NewToken());
                    writer.WriteDictionaryEnd(options);
                }, null, cancellationToken);

            if (created == null)
                return null;

            // A string here, although it is an object path: the portal declares it that way
            var sessionPath = GetString(created, "session_handle");
            _sessionPath = sessionPath;
            LogSessionCreated(sessionPath);

            _sessionWatch = await connection.AddMatchAsync(
                new MatchRule
                {
                    Type = MessageType.Signal,
                    Path = sessionPath,
                    Interface = SessionInterface,
                    Member = "Closed"
                },
                static (Message message, object? _) => message.PathAsString ?? "",
                static (Notification<string> notification) =>
                {
                    if (notification.HasValue)
                        ((XdgDesktopPortalClient)notification.State!).OnSessionClosed(notification.Value);
                },
                false, ObserverFlags.None, this);

            var selected = await RequestAsync(connection, "SelectSources", "oa{sv}", CallTimeout,
                static (ref MessageWriter writer, string token, string? session) =>
                {
                    writer.WriteObjectPath(session!);

                    var options = writer.WriteDictionaryStart();
                    WriteOption(ref writer, "handle_token", token);
                    WriteOption(ref writer, "types", SourceTypeMonitor | SourceTypeWindow);
                    WriteOption(ref writer, "cursor_mode", CursorModeEmbedded);
                    WriteOption(ref writer, "multiple", false);
                    writer.WriteDictionaryEnd(options);
                }, sessionPath, cancellationToken);

            if (selected == null)
            {
                await CloseSessionAsync();
                return null;
            }

            var started = await RequestAsync(connection, "Start", "osa{sv}", InteractiveTimeout,
                static (ref MessageWriter writer, string token, string? session) =>
                {
                    writer.WriteObjectPath(session!);

                    // No parent window: the dialog is simply not attached to ours
                    writer.WriteString("");

                    var options = writer.WriteDictionaryStart();
                    WriteOption(ref writer, "handle_token", token);
                    writer.WriteDictionaryEnd(options);
                }, sessionPath, cancellationToken);

            if (started == null)
            {
                await CloseSessionAsync();
                return null;
            }

            var (nodeId, width, height) = ReadFirstStream(started);
            var remote = await OpenPipeWireRemoteAsync(connection, sessionPath);

            LogStreamGranted(nodeId, width, height);

            return new PipeWireVideoInput(nodeId, remote, width, height);
        }
        catch (Exception exception) when (exception is not (ScreenCastException or OperationCanceledException
                                              or ObjectDisposedException))
        {
            await CloseSessionAsync();
            throw new ScreenCastException(ScreenCastError.PortalFailed, exception.Message, exception);
        }
        catch
        {
            await CloseSessionAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CloseAsync()
    {
        await _gate.WaitAsync();

        try
        {
            await CloseSessionAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DBusConnection> ConnectAsync()
    {
        if (_connection != null)
            return _connection;

        var address = DBusAddress.Session
                      ?? throw new ScreenCastException(ScreenCastError.PortalUnavailable, "No D-Bus session bus");
        var connection = new DBusConnection(address);

        try
        {
            await connection.ConnectAsync();
        }
        catch (Exception exception)
        {
            connection.Dispose();
            throw new ScreenCastException(ScreenCastError.PortalUnavailable, exception.Message, exception);
        }

        _connection = connection;

        return connection;
    }

    private delegate void WriteArguments(ref MessageWriter writer, string token, string? session);

    /// <summary>
    /// Calls a portal method that answers through a Request object: the call itself returns at
    /// once, and the outcome arrives later as the Response signal of that object.
    /// </summary>
    /// <returns>The results of the request, or null when the user cancelled it.</returns>
    private async Task<Dictionary<string, VariantValue>?> RequestAsync(DBusConnection connection, string method,
        string signature, TimeSpan timeout, WriteArguments writeArguments, string? session,
        CancellationToken cancellationToken)
    {
        var token = NewToken();

        // The path is known in advance, so the signal is watched before the call and cannot be missed
        var sender = connection.UniqueName!.TrimStart(':').Replace('.', '_');
        var requestPath = $"{PortalPath}/request/{sender}/{token}";

        var response = new TaskCompletionSource<(uint Code, Dictionary<string, VariantValue> Results)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var rule = new MatchRule
        {
            Type = MessageType.Signal,
            Path = requestPath,
            Interface = RequestInterface,
            Member = "Response"
        };

        using var subscription = await connection.AddMatchAsync(rule,
            static (Message message, object? _) =>
            {
                var reader = message.GetBodyReader();
                var code = reader.ReadUInt32();

                return (code, reader.ReadDictionaryOfStringToVariantValue());
            },
            static (Notification<(uint, Dictionary<string, VariantValue>)> notification) =>
            {
                var completion = (TaskCompletionSource<(uint, Dictionary<string, VariantValue>)>)notification.State!;

                if (notification.HasValue)
                    completion.TrySetResult(notification.Value);
                else if (notification.Exception != null)
                    completion.TrySetException(notification.Exception);
            },
            false, ObserverFlags.EmitOnConnectionClosed, response);

        try
        {
            await connection.CallMethodAsync(CreateCall());
        }
        catch (DBusErrorReplyException exception) when (IsMissingPortal(exception))
        {
            throw new ScreenCastException(ScreenCastError.PortalUnavailable, exception.Message, exception);
        }

        (uint Code, Dictionary<string, VariantValue> Results) result;

        try
        {
            result = await response.Task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException exception)
        {
            throw new ScreenCastException(ScreenCastError.PortalUnavailable, $"{method} was not answered", exception);
        }

        if (result.Code == ResponseCancelled)
        {
            LogCancelled(method);
            return null;
        }

        if (result.Code != ResponseSuccess)
            throw new ScreenCastException(ScreenCastError.PortalFailed, $"{method} ended with response {result.Code}");

        return result.Results;

        MessageBuffer CreateCall()
        {
            var writer = connection.GetMessageWriter();

            writer.WriteMethodCallHeader(Service, PortalPath, ScreenCastInterface, method, signature);
            writeArguments(ref writer, token, session);

            return writer.CreateMessage();
        }
    }

    /// <summary>
    /// The PipeWire connection through which the granted node can be reached. Without it the node
    /// is only visible to clients the PipeWire daemon trusts, which a sandboxed one is not.
    /// </summary>
    private static async Task<SafeFileHandle> OpenPipeWireRemoteAsync(DBusConnection connection, string sessionPath)
    {
        var writer = connection.GetMessageWriter();

        writer.WriteMethodCallHeader(Service, PortalPath, ScreenCastInterface, "OpenPipeWireRemote", "oa{sv}");
        writer.WriteObjectPath(sessionPath);
        writer.WriteDictionaryEnd(writer.WriteDictionaryStart());

        var remote = await connection.CallMethodAsync(writer.CreateMessage(),
            static (Message message, object? _) => message.GetBodyReader().ReadHandle<SafeFileHandle>());

        return remote ?? throw new ScreenCastException(ScreenCastError.PortalFailed, "Portal returned no PipeWire remote");
    }

    /// <summary>
    /// The node and the size of the first stream: a(ua{sv}), one entry per selected source, and
    /// only one can be selected. The size is optional, a portal that leaves it out gives zeros.
    /// </summary>
    private static (uint NodeId, int Width, int Height) ReadFirstStream(Dictionary<string, VariantValue> results)
    {
        if (!results.TryGetValue("streams", out var streams) || streams.Count == 0)
            throw new ScreenCastException(ScreenCastError.PortalFailed, "Portal returned no streams");

        var stream = streams.GetItem(0);
        var nodeId = stream.GetItem(0).GetUInt32();

        try
        {
            var properties = stream.GetItem(1).GetDictionary<string, VariantValue>();

            if (properties.TryGetValue("size", out var size) && size.Count == 2)
                return (nodeId, size.GetItem(0).GetInt32(), size.GetItem(1).GetInt32());
        }
        catch (Exception exception) when (exception is InvalidCastException or InvalidOperationException
                                              or ArgumentException or DBusExceptionBase)
        {
            // The size is a hint, a stream described in an unexpected way is still captured
        }

        return (nodeId, 0, 0);
    }

    private static string GetString(Dictionary<string, VariantValue> results, string key) =>
        results.TryGetValue(key, out var value)
            ? value.GetString()
            : throw new ScreenCastException(ScreenCastError.PortalFailed, $"Portal returned no {key}");

    private static void WriteOption(ref MessageWriter writer, string key, string value)
    {
        writer.WriteDictionaryEntryStart();
        writer.WriteString(key);
        writer.WriteVariantString(value);
    }

    private static void WriteOption(ref MessageWriter writer, string key, uint value)
    {
        writer.WriteDictionaryEntryStart();
        writer.WriteString(key);
        writer.WriteVariantUInt32(value);
    }

    private static void WriteOption(ref MessageWriter writer, string key, bool value)
    {
        writer.WriteDictionaryEntryStart();
        writer.WriteString(key);
        writer.WriteVariantBool(value);
    }

    // Tokens end up in object paths, which allow letters, digits and underscores only
    private static string NewToken() => "flowrate_" + Guid.NewGuid().ToString("N");

    private static bool IsMissingPortal(DBusErrorReplyException exception) =>
        exception.ErrorName is "org.freedesktop.DBus.Error.ServiceUnknown"
            or "org.freedesktop.DBus.Error.UnknownMethod"
            or "org.freedesktop.DBus.Error.UnknownInterface"
            or "org.freedesktop.DBus.Error.NameHasNoOwner";

    /// <summary>
    /// The desktop ended the session by itself: the user stopped the sharing from the system
    /// indicator, or the shared window is gone.
    /// </summary>
    private void OnSessionClosed(string sessionPath)
    {
        if (Interlocked.CompareExchange(ref _sessionPath, null, sessionPath) != sessionPath)
            return;

        LogSessionClosedByDesktop(sessionPath);
        Closed?.Invoke();
    }

    /// <summary>
    /// Closes the portal session, which ends the stream and the sharing indicator of the desktop.
    /// Called under <see cref="_gate"/>.
    /// </summary>
    private async Task CloseSessionAsync()
    {
        // Dropped first, so that closing the session ourselves is not reported as the desktop doing it
        _sessionWatch?.Dispose();
        _sessionWatch = null;

        var sessionPath = Interlocked.Exchange(ref _sessionPath, null);
        var connection = _connection;

        if (sessionPath == null || connection == null)
            return;

        try
        {
            var writer = connection.GetMessageWriter();

            writer.WriteMethodCallHeader(Service, sessionPath, SessionInterface, "Close");

            await connection.CallMethodAsync(writer.CreateMessage());
        }
        catch (Exception exception)
        {
            LogSessionCloseFailed(exception, sessionPath);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Dropping the connection closes the session on the portal side as well
        _sessionWatch?.Dispose();
        _sessionPath = null;
        _connection?.Dispose();
        _connection = null;
    }
}
