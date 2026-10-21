using Microsoft.Extensions.Logging;

namespace Client.Application.Coordinator.Users;

public sealed partial class SavedAccountsRecorder
{
    [LoggerMessage(LogLevel.Warning, "Failed to rename the saved account of user {UserId}")]
    private partial void LogRenameFailed(Exception exception, int userId);
}
