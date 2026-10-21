using Microsoft.Extensions.Logging;

namespace Client.Api;

internal partial class CoordinatorGateway
{
    [LoggerMessage(LogLevel.Warning, "Connection lost: {Reason}")]
    private partial void LogConnectionLost(string reason);

    [LoggerMessage(LogLevel.Trace, "{MessageType} is not handled yet")]
    private partial void LogUnhandledMessage(string messageType);

    [LoggerMessage(LogLevel.Debug, "No media token: {Reason}")]
    private partial void LogMediaTokenFailed(string reason);

    [LoggerMessage(LogLevel.Warning, "{RequestType} not sent, it is invalid: {Details}")]
    private partial void LogInvalidRequestNotSent(string requestType, string details);
}
