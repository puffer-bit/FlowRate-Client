using Microsoft.Extensions.Logging;

namespace Client.GStreamer.WebRTC;

internal sealed partial class WebRTCBinTuning
{
    [LoggerMessage(LogLevel.Warning,
        "[Session {SessionId}] Failed to reach the ICE agent, the SFU may drop the transport after its consent timeout")]
    private partial void LogIceAgentUnreachable(int sessionId);

    [LoggerMessage(LogLevel.Debug, "[Session {SessionId}] ICE keepalives use binding requests: {Enabled}")]
    private partial void LogIceKeepalive(int sessionId, object? enabled);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Failed to enable ICE keepalive requests")]
    private partial void LogIceKeepaliveFailed(Exception exception, int sessionId);
}
