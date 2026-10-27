using Microsoft.Extensions.Logging;

namespace Client.GStreamer.WebRTC;

internal sealed partial class SpeakingIndicator
{
    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to update the speaking indicator")]
    private partial void LogSpeakingUpdateFailed(Exception exception, int sessionId);
}
