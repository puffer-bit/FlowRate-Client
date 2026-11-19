using Microsoft.Extensions.Logging;

namespace Client.Application.Coordinator.Users;

public sealed partial class SavedAccountsRecorder
{
    [LoggerMessage(LogLevel.Warning, "Failed to rename the saved account of user {UserId}")]
    private partial void LogRenameFailed(Exception exception, int userId);

    [LoggerMessage(LogLevel.Warning, "Failed to keep the session token or identity of user {UserId}")]
    private partial void LogCredentialsNotSaved(Exception exception, int userId);
}
