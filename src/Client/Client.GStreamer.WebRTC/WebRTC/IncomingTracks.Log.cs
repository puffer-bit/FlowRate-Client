using Microsoft.Extensions.Logging;
using Shared.Models.Media;

namespace Client.GStreamer.WebRTC;

internal sealed partial class IncomingTracks
{
    [LoggerMessage(LogLevel.Information,
        "[Session {SessionId}] Inbound {MediaType} track of user {UserId} registered, ssrc {Ssrc}")]
    private partial void LogInboundRegistered(int sessionId, MediaType mediaType, int userId, uint ssrc);

    [LoggerMessage(LogLevel.Debug, "[Session {SessionId}] {Count} inbound track(s) ready to be received")]
    private partial void LogInboundsReady(int sessionId, int count);
}
