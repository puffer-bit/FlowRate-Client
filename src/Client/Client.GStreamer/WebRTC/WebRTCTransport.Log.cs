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

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] {Element} failed: {Message} ({Debug})")]
    private partial void LogPipelineError(int sessionId, string element, string message, string? debug);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] {Element} warns: {Message} ({Debug})")]
    private partial void LogPipelineWarning(int sessionId, string element, string message, string? debug);
}
