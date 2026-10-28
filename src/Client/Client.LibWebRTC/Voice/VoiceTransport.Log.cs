using Client.Application.Ports;
using Client.Core.Media;
using Microsoft.Extensions.Logging;
using Shared.Models.Media;
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

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Inbound {MediaType} of user {UserId}, ssrc {Ssrc}")]
    private partial void LogInboundRegistered(int sessionId, MediaType mediaType, int userId, uint ssrc);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Decoding the {Codec} screen of user {UserId}")]
    private partial void LogDecodingVideo(int sessionId, int userId, VideoCodec codec);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] A picture of the screen of user {UserId} could not be decoded")]
    private partial void LogVideoFrameFailed(Exception exception, int sessionId, int userId);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Microphone sent on ssrc {Ssrc}")]
    private partial void LogOutboundStarted(int sessionId, uint ssrc);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Screen sent on ssrc {Ssrc}, encoded by {Encoder}")]
    private partial void LogScreenStarted(int sessionId, uint ssrc, string encoder);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] The screen {Input} could not be captured")]
    private partial void LogCaptureFailed(int sessionId, VideoInput input);

    [LoggerMessage(LogLevel.Debug, "[Session {SessionId}] The screen is encoded at {Kbps} kbit/s")]
    private partial void LogScreenBitrate(int sessionId, int kbps);

    [LoggerMessage(LogLevel.Debug,
        "[Session {SessionId}] Stats: rtt {RoundTripMs:F0} ms, loss in {InboundLoss:F1}%, out {OutboundLoss:F1}%, tcp {IsOverTcp}, received {Streams}")]
    private partial void LogStatsSample(int sessionId, double roundTripMs, double inboundLoss, double outboundLoss,
        bool isOverTcp, string streams);

    [LoggerMessage(LogLevel.Information,
        "[Session {SessionId}] Voice gate at {ThresholdDb:F0} dB, opening from {EffectiveThresholdDb:F0} dB over a " +
        "noise floor of {NoiseFloorDb:F0} dB (null: not adaptive): open {OpenPercent:F0}% of the time, opened " +
        "{Openings} times, loudest peak {LoudestDb:F0} dB, average peak while open {AverageOpenPeakDb:F0} dB")]
    private partial void LogVoiceGateSummary(int sessionId, double thresholdDb, double effectiveThresholdDb,
        double? noiseFloorDb, double openPercent, int openings, double loudestDb, double averageOpenPeakDb);

    [LoggerMessage(LogLevel.Debug,
        "[Session {SessionId}] Counters: available out {AvailableKbps:F0} kbit/s; sent {Sent}; received {Received}")]
    private partial void LogStatsCounters(int sessionId, double availableKbps, string sent, string received);

    [LoggerMessage(LogLevel.Error, "[Session {SessionId}] A callback of libwebrtc failed")]
    private partial void LogCallbackFailed(Exception exception, int sessionId);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Failed to capture the sound of the screen from {Input}")]
    private partial void LogSoundCaptureFailed(int sessionId, AudioInput input);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Sound of the screen sent on ssrc {Ssrc}")]
    private partial void LogScreenSoundStarted(int sessionId, uint ssrc);
}
