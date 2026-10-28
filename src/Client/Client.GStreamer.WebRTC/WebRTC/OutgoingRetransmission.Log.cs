using Microsoft.Extensions.Logging;

namespace Client.GStreamer.WebRTC;

internal sealed partial class OutgoingRetransmission
{
    [LoggerMessage(LogLevel.Debug,
        "[Session {SessionId}] Lost packets of ssrc {Ssrc} are resent on ssrc {RtxSsrc}, payload type {RtxPayloadType}")]
    private partial void LogTrackRetransmitted(int sessionId, uint ssrc, uint rtxSsrc, int rtxPayloadType);

    [LoggerMessage(LogLevel.Warning,
        "[Session {SessionId}] Failed to create rtprtxsend, lost packets of our tracks are not resent")]
    private partial void LogSenderCreateFailed(int sessionId);

    [LoggerMessage(LogLevel.Warning,
        "[Session {SessionId}] Failed to set up the retransmission of our tracks, lost packets are not resent")]
    private partial void LogSenderSetupFailed(Exception exception, int sessionId);
}
