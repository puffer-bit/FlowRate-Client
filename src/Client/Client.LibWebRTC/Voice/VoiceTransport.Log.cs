using Microsoft.Extensions.Logging;
using Shared.Enums.WebRTC;

namespace Client.LibWebRTC.Voice;

public sealed partial class VoiceTransport
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
    private partial void LogConnectionState(int sessionId, WebRTCConnectionState state);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Inbound voice of user {UserId}, ssrc {Ssrc}")]
    private partial void LogInboundRegistered(int sessionId, int userId, uint ssrc);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Microphone sent on ssrc {Ssrc}")]
    private partial void LogOutboundStarted(int sessionId, uint ssrc);

    [LoggerMessage(LogLevel.Debug,
        "[Session {SessionId}] Stats: rtt {RoundTripMs:F0} ms, loss in {InboundLoss:F1}%, out {OutboundLoss:F1}%, tcp {IsOverTcp}, received {Streams}")]
    private partial void LogStatsSample(int sessionId, double roundTripMs, double inboundLoss, double outboundLoss,
        bool isOverTcp, string streams);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] A callback of libwebrtc failed")]
    private partial void LogCallbackFailed(Exception exception, int sessionId);
}
