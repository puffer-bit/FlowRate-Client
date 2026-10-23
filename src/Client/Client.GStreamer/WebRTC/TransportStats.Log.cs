using Microsoft.Extensions.Logging;

namespace Client.GStreamer.WebRTC;

internal sealed partial class TransportStats
{
    [LoggerMessage(LogLevel.Debug,
        "[Session {SessionId}] In  user {UserId} {Source} ssrc {Ssrc}: {Kbps:F0} kbit/s, {PacketRate:F0} pkt/s, " +
        "lost {Lost} ({LossPercent:F1}%), jitter {JitterMs:F1} ms, late {Late}, NACK sent {Nacks}, PLI sent {Plis}")]
    private partial void LogInboundStats(int sessionId, int userId, string source, uint ssrc, double kbps,
        double packetRate, long lost, double lossPercent, double jitterMs, long late, uint nacks, uint plis);

    [LoggerMessage(LogLevel.Debug,
        "[Session {SessionId}] Out {Source} ssrc {Ssrc}: {Kbps:F0} kbit/s, {PacketRate:F0} pkt/s, " +
        "SFU reports loss {LossPercent:F1}%, jitter {JitterMs:F1} ms, RTT {RoundTripMs:F0} ms, " +
        "NACK received {Nacks}, PLI received {Plis}")]
    private partial void LogOutboundStats(int sessionId, string source, uint ssrc, double kbps, double packetRate,
        double lossPercent, double jitterMs, double roundTripMs, uint nacks, uint plis);

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Media goes over {Local} -> {Remote}")]
    private partial void LogCandidatePair(int sessionId, string local, string remote);

    [LoggerMessage(LogLevel.Warning,
        "[Session {SessionId}] ICE moved the media to another candidate pair: {Local} -> {Remote}")]
    private partial void LogCandidatePairChanged(int sessionId, string local, string remote);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Failed to request the transport statistics")]
    private partial void LogStatsRequestFailed(Exception exception, int sessionId);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] Failed to read the transport statistics")]
    private partial void LogStatsReadFailed(Exception exception, int sessionId);
}
