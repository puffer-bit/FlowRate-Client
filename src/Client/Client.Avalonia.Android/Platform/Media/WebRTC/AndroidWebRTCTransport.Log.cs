using System;
using Microsoft.Extensions.Logging;

namespace Client.Droid.Platform.Media.WebRTC;

internal sealed partial class AndroidWebRTCTransport
{
    [LoggerMessage(LogLevel.Debug, "[Session {SessionId}] Remote description:\n{Sdp}")]
    private partial void LogRemoteDescription(int sessionId, string sdp);

    [LoggerMessage(LogLevel.Debug, "[Session {SessionId}] Local description:\n{Sdp}")]
    private partial void LogLocalDescription(int sessionId, string sdp);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] Negotiation failed: {Error}")]
    private partial void LogNegotiationFailed(int sessionId, string error);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] The answer carries no fingerprint")]
    private partial void LogLocalFingerprintMissing(int sessionId);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Peer connection {State}")]
    private partial void LogConnectionState(int sessionId, string state);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Inbound voice of user {UserId}, ssrc {Ssrc}")]
    private partial void LogInboundRegistered(int sessionId, int userId, uint ssrc);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Microphone sent on ssrc {Ssrc}")]
    private partial void LogOutboundStarted(int sessionId, uint ssrc);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] A callback of libwebrtc failed")]
    private partial void LogCallbackFailed(Exception exception, int sessionId);
}
