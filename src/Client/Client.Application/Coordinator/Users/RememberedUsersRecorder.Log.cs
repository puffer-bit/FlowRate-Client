using Microsoft.Extensions.Logging;

namespace Client.Application.Coordinator.Users;

public sealed partial class RememberedUsersRecorder
{
    [LoggerMessage(LogLevel.Warning, "Failed to remember user {UserId}")]
    private partial void LogRememberFailed(Exception exception, int userId);
}
