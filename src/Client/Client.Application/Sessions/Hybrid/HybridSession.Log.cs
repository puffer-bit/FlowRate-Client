using Client.Application.Ports;
using Client.Core.Sessions.Hybrid;
using Microsoft.Extensions.Logging;
using Shared.Enums;

namespace Client.Application.Sessions.Hybrid;

public partial class HybridSession
{
    [LoggerMessage(LogLevel.Debug, "[Session {SessionId}] State changed to {State}")]
    private partial void LogStateChanged(int sessionId, HybridSessionState state);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] DTLS parameters accepted by the SFU")]
    private partial void LogDtlsAccepted(int sessionId);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to start sending {Input}")]
    private partial void LogStartSendingFailed(int sessionId, AudioInput input);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Voice started, ssrc {Ssrc}")]
    private partial void LogVoiceStarted(int sessionId, uint ssrc);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Voice stopped")]
    private partial void LogVoiceStopped(int sessionId);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to start sending {Input}")]
    private partial void LogScreenShareStartFailed(int sessionId, VideoInput input);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Screen share started, ssrc {Ssrc}")]
    private partial void LogScreenShareStarted(int sessionId, uint ssrc);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Screen share changed, ssrc {Ssrc}")]
    private partial void LogScreenShareChanged(int sessionId, uint ssrc);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Screen of user {UserId} watched: {IsWatched}")]
    private partial void LogScreenWatchChanged(int sessionId, int userId, bool isWatched);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Screen audio started, ssrc {Ssrc}")]
    private partial void LogScreenAudioStarted(int sessionId, uint ssrc);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Failed to send the screen audio from {Input}, the screen is shared silent")]
    private partial void LogScreenAudioStartFailed(int sessionId, AudioInput input);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Screen share stopped")]
    private partial void LogScreenShareStopped(int sessionId);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Transport lost: {Reason}")]
    private partial void LogTransportLost(int sessionId, string reason);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Transport recovered on its own")]
    private partial void LogTransportRecovered(int sessionId);

    [LoggerMessage(LogLevel.Debug,
        "[Session {SessionId}] Reconnect attempt {Attempt} skipped: the coordinator is not connected")]
    private partial void LogReconnectAttemptSkipped(int sessionId, int attempt);

    [LoggerMessage(LogLevel.Debug, "[Session {SessionId}] Waiting for the server to give the session an SFU node")]
    private partial void LogWaitingForNode(int sessionId);

    [LoggerMessage(LogLevel.Debug, "[Session {SessionId}] Reconnect attempt {Attempt} started")]
    private partial void LogReconnectAttemptStarted(int sessionId, int attempt);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Reconnected after {Attempts} attempt(s)")]
    private partial void LogReconnected(int sessionId, int attempts);

    [LoggerMessage(LogLevel.Debug, "[Session {SessionId}] Reconnect canceled")]
    private partial void LogReconnectCanceled(int sessionId);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Reconnect stopped")]
    private partial void LogReconnectStopped(Exception exception, int sessionId);

    // The message only: it repeats on every reconnect attempt
    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Rejoin failed: {Error}")]
    private partial void LogRejoinFailed(int sessionId, string error);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Rejoin rejected: {Result}")]
    private partial void LogRejoinRejected(int sessionId, HybridSessionJoinResult result);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Failed to start the new WebRTC transport")]
    private partial void LogNewTransportFailed(int sessionId);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Failed to resume the inbound tracks: {Result}, attempt {Attempt}")]
    private partial void LogResumeInboundsFailed(int sessionId, ResumeInboundsResult result, int attempt);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Resuming the inbound tracks stopped")]
    private partial void LogResumeInboundsStopped(Exception exception, int sessionId);

    [LoggerMessage(LogLevel.Debug, "[Session {SessionId}] Closed")]
    private partial void LogClosed(int sessionId);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to handle {Event}")]
    private partial void LogPeerEventFailed(Exception exception, int sessionId, string @event);
}
