using System.Net;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using Client.Api.Protocol;
using Client.WebSockets;
using Shared.Models;
using Shared.Protocol;
using Client.Core.Coordinator;
using Client.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Client.Api.Transport;

internal partial class WebSocketGateway : IWebSocketGateway
{
    public event Action<BaseMessage>? MessageReceived;
    public event Action<string>? ConnectionLost;

    private WebSocketConnection? Connection { get; set; }

    private readonly IMessageParser _messageParser;
    private readonly ILogger<WebSocketGateway> _logger;

    public WebSocketGateway(IMessageParser messageParser, ILogger<WebSocketGateway> logger)
    {
        _messageParser = messageParser;
        _logger = logger;
    }

    public async Task<TransportConnectResult> ConnectAsync(string address, int port, string? trustedFingerprint,
        CancellationToken cancellationToken = default)
    {
        // A previous socket is still open after a timeout or a failed authentication: it is closed
        // here, otherwise the server keeps it as a connection nobody uses
        await CloseConnectionAsync();

        ServerCertificateModel? rejectedCertificate = null;
        var isCertificateVerified = false;

        // Runs during the TLS handshake, so an untrusted server never receives the login data
        bool ValidateCertificate(object _, X509Certificate? certificate, X509Chain? __, SslPolicyErrors errors)
        {
            if (ServerCertificateCheck.IsTrusted(certificate, errors, trustedFingerprint, out var fingerprint))
            {
                isCertificateVerified = errors == SslPolicyErrors.None;
                return true;
            }
            if (certificate == null || fingerprint == null)
                return false;

            using var details = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
            rejectedCertificate = new ServerCertificateModel(address, port, fingerprint, details.Subject,
                details.NotAfter.ToUniversalTime());
            return false;
        }

        var options = new WebSocketConnectionOptions
        {
            Configure = socketOptions =>
            {
                socketOptions.RemoteCertificateValidationCallback = ValidateCertificate;
                socketOptions.SetRequestHeader(ProtocolVersion.Header, ProtocolVersion.Format(ProtocolVersion.Current));
            }
        };

        var connection = new WebSocketConnection(options);
        connection.MessageReceived += ProcessMessage;

        try
        {
            await connection.ConnectAsync(new Uri($"wss://{address}:{port}/"), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await connection.DisposeAsync();
            throw;
        }
        catch (Exception ex)
        {
            var mismatch = GetProtocolMismatch(connection);
            await connection.DisposeAsync();
            if (mismatch != null)
                LogProtocolMismatch(ProtocolVersion.Current, mismatch.ServerMinVersion, mismatch.ServerVersion);
            return new TransportConnectResult(false, ex.Message, rejectedCertificate, mismatch);
        }

        // Subscribed only once connected, so a failed attempt is reported by its result, not as a lost connection
        connection.Disconnected += ProcessDisconnection;
        Connection = connection;
        return new TransportConnectResult(true, IsCertificateVerified: isCertificateVerified);
    }

    public Task DisconnectAsync() => CloseConnectionAsync();

    // The server refuses a version it does not serve before the upgrade and names the versions it does
    private static ProtocolMismatch? GetProtocolMismatch(WebSocketConnection connection)
    {
        if (connection.HandshakeStatusCode != HttpStatusCode.UpgradeRequired)
            return null;

        var headers = connection.HandshakeResponseHeaders;
        return new ProtocolMismatch(ReadVersion(ProtocolVersion.Header), ReadVersion(ProtocolVersion.MinHeader));

        int ReadVersion(string name) =>
            headers != null && headers.TryGetValue(name, out var values)
                                && ProtocolVersion.TryParse(values.FirstOrDefault(), out var version)
                ? version
                : 0;
    }

    private async Task CloseConnectionAsync()
    {
        var connection = Connection;
        Connection = null;
        if (connection == null)
            return;

        // Our own close does not raise Disconnected, so it is not reported as a lost connection
        try
        {
            await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "Normal closing");
        }
        catch (Exception ex)
        {
            LogCloseFailed(ex);
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    public bool SendMessage(BaseMessage message)
    {
        var connection = Connection;

        if (connection is not { IsOpen: true })
        {
            LogSendWithoutConnection(message.GetType().Name);
            return false;
        }

        if (!connection.SendText(_messageParser.EncodeBaseMessage(message)))
        {
            LogSendRejected(message.GetType().Name);
            return false;
        }

        return true;
    }

    private void ProcessMessage(WebSocketMessage received)
    {
        if (received.Type != WebSocketMessageType.Text)
        {
            LogNonTextMessageDropped(received.Type);
            return;
        }

        // Exceptions stop here: escaping into the connection they would close it
        BaseMessage message;
        try
        {
            message = _messageParser.DecodeBaseMessage(received.GetText());
        }
        catch
        {
            // The parser has logged it; a malformed message is dropped
            return;
        }

        try
        {
            MessageReceived?.Invoke(message);
        }
        catch (Exception ex)
        {
            LogHandlingFailed(ex, message.GetType().Name);
        }
    }

    private void ProcessDisconnection(WebSocketDisconnectInfo info)
    {
        ConnectionLost?.Invoke(info.Reason);
    }

    public void Dispose()
    {
        var connection = Connection;
        Connection = null;
        if (connection != null)
            _ = connection.DisposeAsync().AsTask();
    }
}
