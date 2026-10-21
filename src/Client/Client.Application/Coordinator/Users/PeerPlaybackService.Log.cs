using Microsoft.Extensions.Logging;

namespace Client.Application.Coordinator.Users;

public sealed partial class PeerPlaybackService
{
    [LoggerMessage(LogLevel.Warning, "Failed to load the playback settings of {Count} users")]
    private partial void LogLoadFailed(Exception exception, int count);

    [LoggerMessage(LogLevel.Warning, "Failed to save the playback settings of user {UserId}")]
    private partial void LogSaveFailed(Exception exception, int userId);

    [LoggerMessage(LogLevel.Debug, "Playback settings of user {UserId} not saved, the user is not remembered yet")]
    private partial void LogUserNotRemembered(int userId);
}
