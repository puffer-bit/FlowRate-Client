using Microsoft.Extensions.Logging;

namespace Client.Infrastructure.Database;

internal sealed partial class DatabaseInitializer
{
    [LoggerMessage(LogLevel.Critical, "{FilePath} is corrupted")]
    private partial void LogCorrupted(string filePath);

    [LoggerMessage(LogLevel.Critical, "{FilePath} is corrupted")]
    private partial void LogCorrupted(Exception exception, string filePath);

    [LoggerMessage(LogLevel.Critical, "{FilePath} is locked by another process")]
    private partial void LogInUse(string filePath);

    [LoggerMessage(LogLevel.Critical, "The database has schema version {Version}, this build knows up to {LatestVersion}")]
    private partial void LogNewerSchema(int version, int latestVersion);

    [LoggerMessage(LogLevel.Critical, "Database initialization failed")]
    private partial void LogInitializationFailed(Exception exception);

    [LoggerMessage(LogLevel.Warning, "The Entity Framework database was moved to {BackupPath}, a new one is created")]
    private partial void LogEntityFrameworkDatabaseMoved(string backupPath);

    [LoggerMessage(LogLevel.Information, "Database backed up to {BackupPath} before the upgrade")]
    private partial void LogBackedUp(string backupPath);

    [LoggerMessage(LogLevel.Information, "Migration {Version} {Name} applied")]
    private partial void LogMigrationApplied(int version, string name);
}
