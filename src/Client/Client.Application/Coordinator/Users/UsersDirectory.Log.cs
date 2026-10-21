using Microsoft.Extensions.Logging;

namespace Client.Application.Coordinator.Users;

public sealed partial class UsersDirectory
{
    [LoggerMessage(LogLevel.Warning, "Failed to get {Count} users")]
    private partial void LogGetUsersFailed(Exception exception, int count);

    [LoggerMessage(LogLevel.Warning, "The server refused to give {Count} users")]
    private partial void LogGetUsersRefused(int count);

    [LoggerMessage(LogLevel.Warning, "Failed to search users")]
    private partial void LogSearchUsersFailed(Exception exception);
}
