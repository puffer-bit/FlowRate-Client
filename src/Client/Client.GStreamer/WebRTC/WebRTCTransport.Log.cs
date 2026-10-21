using Microsoft.Extensions.Logging;

namespace Client.GStreamer.WebRTC;

public sealed partial class WebRTCTransport
{
    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to create webrtcbin. Check GStreamer plugin availability")]
    private partial void LogWebrtcbinCreateFailed(int sessionId);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Incoming stream on {Pad}: {Caps}")]
    private partial void LogIncomingStream(int sessionId, string pad, string caps);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Incoming stream on {Pad} ended")]
    private partial void LogIncomingStreamEnded(int sessionId, string pad);
}
