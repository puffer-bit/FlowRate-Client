using Microsoft.Extensions.Logging;

namespace Client.Api.Protocol;

internal partial class MessageParser
{
    [LoggerMessage(LogLevel.Error, "Failed to encode {MessageType} to JSON")]
    private partial void LogEncodeFailed(Exception exception, string messageType);

    [LoggerMessage(LogLevel.Error, "Failed to decode a message from JSON")]
    private partial void LogDecodeFailed(Exception exception);
}
