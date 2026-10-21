using System.Net.WebSockets;
using Microsoft.Extensions.Logging;

namespace Client.Api.Transport;

internal partial class WebSocketGateway
{
    [LoggerMessage(LogLevel.Warning, "Failed to close the web socket gracefully")]
    private partial void LogCloseFailed(Exception exception);

    [LoggerMessage(LogLevel.Error, "{MessageType} not sent: no open connection")]
    private partial void LogSendWithoutConnection(string messageType);

    [LoggerMessage(LogLevel.Error, "{MessageType} not sent: the connection closed or its send queue is full")]
    private partial void LogSendRejected(string messageType);

    [LoggerMessage(LogLevel.Warning, "Dropped a {MessageType} message: the protocol is JSON text")]
    private partial void LogNonTextMessageDropped(WebSocketMessageType messageType);

    [LoggerMessage(LogLevel.Warning,
        "The server does not serve protocol version {ClientVersion}: it serves {MinVersion} to {ServerVersion} (0 = not given)")]
    private partial void LogProtocolMismatch(int clientVersion, int minVersion, int serverVersion);

    [LoggerMessage(LogLevel.Error, "Handling {MessageType} failed")]
    private partial void LogHandlingFailed(Exception exception, string messageType);
}
