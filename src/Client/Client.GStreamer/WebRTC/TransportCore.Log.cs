using Microsoft.Extensions.Logging;

namespace Client.GStreamer.WebRTC;

internal sealed partial class TransportCore
{
    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Failed to handle a WebRTC notification")]
    private partial void LogNotificationFailed(Exception exception, int sessionId);
}
