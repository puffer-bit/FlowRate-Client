using System.Globalization;
using System.Reflection;
using Client.Application.Ports;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Client.Infrastructure.Database;

/// <summary>
/// Brings the database file to the schema this build expects. Migrations are the embedded
/// <c>Migrations/NNN_Name.sql</c> scripts; the number of the last applied one is kept in
/// <c>PRAGMA user_version</c>.
/// </summary>
internal sealed partial class DatabaseInitializer : IDatabaseInitializer
{
    private const string MigrationResourcePrefix = "Client.Infrastructure.Database.Migrations.";

    // SQLITE_BUSY, SQLITE_LOCKED, SQLITE_CORRUPT and SQLITE_NOTADB
    private const int BusyErrorCode = 5;
    private const int LockedErrorCode = 6;
    private const int CorruptErrorCode = 11;
    private const int NotADatabaseErrorCode = 26;

    // A file another process holds locked never frees up, there is no point in waiting long
    private const int ProbeBusyTimeoutSeconds = 2;

    private readonly SqliteDatabase _database;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(SqliteDatabase database, ILogger<DatabaseInitializer> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task<DatabaseState> VerifyDatabaseAsync()
    {
        try
        {
            if (await IsInUseAsync())
            {
                LogInUse(_database.FilePath);
                return DatabaseState.InUse;
            }

            await SetAsideEntityFrameworkDatabaseAsync();

            await using var connection = await _database.OpenAsync();

            if (!await IsIntactAsync(connection))
            {
                LogCorrupted(_database.FilePath);
                return DatabaseState.Corrupted;
            }

            // Persistent in the file, and cannot change inside a transaction
            await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;");

            var migrations = LoadMigrations();
            var latestVersion = migrations.Count == 0 ? 0 : migrations[^1].Version;
            var version = Convert.ToInt32(await ScalarAsync(connection, "PRAGMA user_version;"), CultureInfo.InvariantCulture);

            if (version > latestVersion)
            {
                LogNewerSchema(version, latestVersion);
                return DatabaseState.NewerThanApplication;
            }

            if (version < latestVersion && version > 0)
                await BackUpAsync(connection, version);

            foreach (var migration in migrations.Where(m => m.Version > version))
                await ApplyAsync(connection, migration);

            return DatabaseState.NoError;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is CorruptErrorCode or NotADatabaseErrorCode)
        {
            LogCorrupted(exception, _database.FilePath);
            return DatabaseState.Corrupted;
        }
        catch (Exception exception)
        {
            LogInitializationFailed(exception);
            return DatabaseState.UnexpectedError;
        }
    }

    /// <summary>
    /// Whether another process holds the file locked, see <see cref="IDatabaseProtection.LockFile"/>.
    /// The pooled connections would wait for it the full command timeout.
    /// </summary>
    private async Task<bool> IsInUseAsync()
    {
        try
        {
            await using var connection = await SqliteDatabase.OpenAsync(
                _database.BuildProbe(_database.Password, ProbeBusyTimeoutSeconds));

            await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master;");
            return false;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is BusyErrorCode or LockedErrorCode)
        {
            return true;
        }
    }

    private static async Task<bool> IsIntactAsync(SqliteConnection connection) =>
        await ScalarAsync(connection, "PRAGMA quick_check;") is "ok";

    /// <summary>
    /// A database created by the Entity Framework builds has another schema and holds nothing worth
    /// carrying over before the first release, so it is moved aside and a new one is created.
    /// </summary>
    private async Task SetAsideEntityFrameworkDatabaseAsync()
    {
        if (!File.Exists(_database.FilePath))
            return;

        await using (var connection = await _database.OpenAsync())
        {
            var table = await ScalarAsync(connection,
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory';");

            if (table == null)
                return;
        }

        // Pooled connections keep the file open
        SqliteConnection.ClearAllPools();

        var backupPath = _database.FilePath + ".ef-backup";

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var source = _database.FilePath + suffix;

            if (File.Exists(source))
                File.Move(source, backupPath + suffix, overwrite: true);
        }

        LogEntityFrameworkDatabaseMoved(backupPath);
    }

    /// <summary>
    /// Copies the database before its schema changes, so a failed upgrade loses nothing.
    /// </summary>
    private async Task BackUpAsync(SqliteConnection connection, int version)
    {
        var backupPath = $"{_database.FilePath}.v{version}.bak";

        // VACUUM INTO refuses to overwrite a file
        File.Delete(backupPath);

        await using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $path;";
        command.Parameters.AddWithValue("$path", backupPath);
        await command.ExecuteNonQueryAsync();

        LogBackedUp(backupPath);
    }

    /// <summary>
    /// The script and the new version are committed together, so a failed script leaves the
    /// database at the previous version.
    /// </summary>
    private async Task ApplyAsync(SqliteConnection connection, Migration migration)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = migration.Script;
            await command.ExecuteNonQueryAsync();

            // PRAGMA takes no parameters; the version is a number parsed from the resource name
            command.CommandText = $"PRAGMA user_version = {migration.Version.ToString(CultureInfo.InvariantCulture)};";
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        LogMigrationApplied(migration.Version, migration.Name);
    }

    private static List<Migration> LoadMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var migrations = new List<Migration>();

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(MigrationResourcePrefix, StringComparison.Ordinal) ||
                !resource.EndsWith(".sql", StringComparison.Ordinal))
                continue;

            // "001_Initial.sql"
            var fileName = resource[MigrationResourcePrefix.Length..^".sql".Length];
            var separator = fileName.IndexOf('_');

            if (separator <= 0 || !int.TryParse(fileName[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var version))
                throw new InvalidOperationException($"Migration {resource} is not named NNN_Name.sql");

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);

            migrations.Add(new Migration(version, fileName[(separator + 1)..], reader.ReadToEnd()));
        }

        migrations.Sort((left, right) => left.Version.CompareTo(right.Version));

        for (var i = 0; i < migrations.Count; i++)
        {
            if (migrations[i].Version != i + 1)
                throw new InvalidOperationException($"Migrations must be numbered 1, 2, 3... without gaps, found {migrations[i].Version} at position {i + 1}");
        }

        return migrations;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private sealed record Migration(int Version, string Name, string Script);
}
