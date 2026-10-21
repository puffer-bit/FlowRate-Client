using Shared.Enums.Coordinator;
using Microsoft.Extensions.Logging;

namespace Client.Application.Coordinator;

public partial class CoordinatorSession
{
    // Every message starts with the coordinator name: a client may hold several coordinators at once
    private string LogName => GetCoordinatorName();

    [LoggerMessage(LogLevel.Debug, "[{Coordinator}] {SessionType} session {SessionId} created: {SessionName}")]
    private partial void LogSessionAdded(string coordinator, string sessionType, int sessionId, string sessionName);

    [LoggerMessage(LogLevel.Debug, "[{Coordinator}] {SessionType} session {SessionId} reconfigured: {SessionName}")]
    private partial void LogSessionReconfigured(string coordinator, string sessionType, int sessionId, string sessionName);

    [LoggerMessage(LogLevel.Debug, "[{Coordinator}] {SessionType} session {SessionId} deleted")]
    private partial void LogSessionDeleted(string coordinator, string sessionType, int sessionId);

    [LoggerMessage(LogLevel.Warning, "[{Coordinator}] {Details}")]
    private partial void LogRequestRejected(string coordinator, string details);

    [LoggerMessage(LogLevel.Debug, "[{Coordinator}] Connect attempt to {Address}:{Port} started")]
    private partial void LogConnectStarted(string coordinator, string address, int port);

    [LoggerMessage(LogLevel.Information, "[{Coordinator}] Certificate {Fingerprint} trusted for {Host}:{Port}")]
    private partial void LogCertificateTrusted(string coordinator, string fingerprint, string host, int port);

    [LoggerMessage(LogLevel.Debug, "[{Coordinator}] Connection to coordinator {CoordinatorId} closed")]
    private partial void LogDisconnected(string coordinator, string? coordinatorId);

    [LoggerMessage(LogLevel.Debug, "[{Coordinator}] Connection closed until the application is back")]
    private partial void LogSuspended(string coordinator);

    [LoggerMessage(LogLevel.Warning, "[{Coordinator}] Connection lost: {Reason}")]
    private partial void LogConnectionLost(string coordinator, string reason);

    [LoggerMessage(LogLevel.Debug, "[{Coordinator}] Reconnect attempt {Attempt} started")]
    private partial void LogReconnectAttemptStarted(string coordinator, int attempt);

    // The message only: every attempt failing with a full stack would flood the log
    [LoggerMessage(LogLevel.Warning, "[{Coordinator}] Reconnect attempt {Attempt} failed: {Error}")]
    private partial void LogReconnectAttemptFailed(string coordinator, int attempt, string error);

    [LoggerMessage(LogLevel.Information, "[{Coordinator}] Reconnected after {Attempts} attempt(s)")]
    private partial void LogReconnected(string coordinator, int attempts);

    [LoggerMessage(LogLevel.Debug, "[{Coordinator}] Connect attempt canceled")]
    private partial void LogReconnectCanceled(string coordinator);

    [LoggerMessage(LogLevel.Error, "[{Coordinator}] Handling {Message} failed, the connection is closed")]
    private partial void LogConnectionHandlingFailed(Exception exception, string coordinator, string message);

    [LoggerMessage(LogLevel.Debug, "[{Coordinator}] Network changed, available: {IsAvailable}")]
    private partial void LogNetworkChanged(string coordinator, bool isAvailable);

    [LoggerMessage(LogLevel.Warning,
        "[{Coordinator}] Certificate {Fingerprint} of {Address}:{Port} cannot be verified and is not trusted yet")]
    private partial void LogCertificateNotTrusted(string coordinator, string fingerprint, string address, int port);

    [LoggerMessage(LogLevel.Warning,
        "[{Coordinator}] Certificate of {Address}:{Port} changed from {OldFingerprint} to {NewFingerprint}")]
    private partial void LogCertificateChanged(string coordinator, string address, int port, string oldFingerprint,
        string newFingerprint);

    [LoggerMessage(LogLevel.Debug, "[{Coordinator}] Connect attempt failed: {Error}")]
    private partial void LogConnectFailed(string coordinator, string? error);

    [LoggerMessage(LogLevel.Debug, "[{Coordinator}] Auth attempt started")]
    private partial void LogAuthStarted(string coordinator);

    [LoggerMessage(LogLevel.Debug, "[{Coordinator}] Auth attempt failed")]
    private partial void LogAuthFailed(string coordinator);

    [LoggerMessage(LogLevel.Debug, "[{Coordinator}] Connected to coordinator {CoordinatorId}")]
    private partial void LogConnected(string coordinator, string coordinatorId);

    [LoggerMessage(LogLevel.Debug, "[{Coordinator}] Auth attempt timed out")]
    private partial void LogAuthTimedOut(string coordinator);

    [LoggerMessage(LogLevel.Debug, "[{Coordinator}] Auth attempt canceled")]
    private partial void LogAuthCanceled(string coordinator);

    [LoggerMessage(LogLevel.Warning,
        "[{Coordinator}] {Host}:{Port} is bound to coordinator {BoundId}, but coordinator {AnsweredId} answered")]
    private partial void LogCoordinatorMismatch(string coordinator, string host, int port, string? boundId,
        string answeredId);

    [LoggerMessage(LogLevel.Warning, "[{Coordinator}] Roles not loaded: {Result}")]
    private partial void LogRolesNotLoaded(string coordinator, CoordinatorRoleResult result);
}
