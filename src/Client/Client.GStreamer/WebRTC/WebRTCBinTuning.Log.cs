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

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Failed to tune the RTP manager")]
    private partial void LogRtpBinTuningFailed(Exception exception, int sessionId);

    [LoggerMessage(LogLevel.Debug, "[Session {SessionId}] Video ssrc {Ssrc} is buffered for {Latency} ms")]
    private partial void LogVideoJitterBuffer(int sessionId, uint ssrc, uint latency);

    [LoggerMessage(LogLevel.Debug, "[Session {SessionId}] RTCP may take up to {Budget} bytes/s")]
    private partial void LogFeedbackBudget(int sessionId, int budget);
}
