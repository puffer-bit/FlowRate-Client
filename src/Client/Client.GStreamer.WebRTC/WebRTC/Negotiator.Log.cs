using Microsoft.Extensions.Logging;

namespace Client.GStreamer.WebRTC;

internal sealed partial class Negotiator
{
    [LoggerMessage(LogLevel.Debug, "[Session {SessionId}] Remote description built:\n{Sdp}")]
    private partial void LogRemoteDescriptionBuilt(int sessionId, string sdp);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to parse the generated remote description")]
    private partial void LogRemoteDescriptionParseFailed(int sessionId);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to apply the remote description: {Error}")]
    private partial void LogRemoteDescriptionApplyFailed(int sessionId, string error);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to attach the outbound tracks")]
    private partial void LogOutboundAttachFailed(Exception exception, int sessionId);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to create the answer: {Error}")]
    private partial void LogAnswerCreateFailed(int sessionId, string error);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to create the answer: no answer in the reply")]
    private partial void LogAnswerMissing(int sessionId);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to apply the answer")]
    private partial void LogAnswerApplyFailed(Exception exception, int sessionId);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to apply the local description: {Error}")]
    private partial void LogLocalDescriptionApplyFailed(int sessionId, string error);

    [LoggerMessage(LogLevel.Debug, "[Session {SessionId}] Local description:\n{Sdp}")]
    private partial void LogLocalDescription(int sessionId, string? sdp);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to find the local fingerprint in the answer")]
    private partial void LogLocalFingerprintMissing(int sessionId);

    [LoggerMessage(LogLevel.Information,
        "[Session {SessionId}] Local DTLS role: {Role}, fingerprint: {Algorithm} {Fingerprint}")]
    private partial void LogLocalDtlsParameters(int sessionId, string role, string algorithm, string fingerprint);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Failed to handle a WebRTC promise")]
    private partial void LogPromiseFailed(Exception exception, int sessionId);

    [LoggerMessage(LogLevel.Warning,
        "[Session {SessionId}] Failed to have section {Mline} ask for lost packets, its losses wait for a keyframe")]
    private partial void LogRetransmissionRequestsFailed(Exception exception, int sessionId, int mline);
}
