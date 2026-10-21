using Client.Application.Ports;
using Microsoft.Data.Sqlite;

namespace Client.Infrastructure.Database;

/// <summary>
/// Encrypts the database file with SQLite3 Multiple Ciphers (its default ChaCha20-Poly1305 scheme)
/// and holds it locked with the exclusive VFS.
/// </summary>
internal sealed class DatabaseProtection : IDatabaseProtection
{
    // SQLITE_BUSY, SQLITE_LOCKED and SQLITE_NOTADB: a wrong password gives the last one
    private const int BusyErrorCode = 5;
    private const int LockedErrorCode = 6;
    private const int NotADatabaseErrorCode = 26;

    // A file another process holds locked never frees up, there is no point in waiting long
    private const int UnlockBusyTimeoutSeconds = 2;

    // Encrypting rewrites the whole file, a repository writing at that moment is waited for
    private const int RekeyBusyTimeoutSeconds = 30;

    private readonly SqliteDatabase _database;

    public DatabaseProtection(SqliteDatabase database)
    {
        _database = database;
    }

    public bool IsEncrypted => _database.IsFileEncrypted;

    public bool CanLockFile => SqliteDatabase.CanLockFile;

    public async Task<bool> UnlockAsync(string password)
    {
        try
        {
            await using var connection = await SqliteDatabase.OpenAsync(
                _database.BuildProbe(password, UnlockBusyTimeoutSeconds));

            // The key is checked on the first read of a page
            await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master;");
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == NotADatabaseErrorCode)
        {
            return false;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is BusyErrorCode or LockedErrorCode)
        {
            throw new DatabaseInUseException(exception);
        }

        _database.Configure(password, _database.IsFileLocked);
        return true;
    }

    public async Task ChangePasswordAsync(string? password)
    {
        // Idle pooled connections would keep reading the file the old way
        SqliteConnection.ClearAllPools();

        await using (var connection = await SqliteDatabase.OpenAsync(
                         _database.BuildProbe(_database.Password, RekeyBusyTimeoutSeconds)))
        {
            // SQLite3 Multiple Ciphers does not rekey a database in WAL mode
            if (await ScalarAsync(connection, "PRAGMA journal_mode = DELETE;") is not "delete")
                throw new InvalidOperationException("The database stays in WAL mode: another connection uses it");

            try
            {
                // PRAGMA takes no parameters; an empty key decrypts the file
                await ScalarAsync(connection, $"PRAGMA rekey = '{(password ?? "").Replace("'", "''")}';");
            }
            finally
            {
                await ScalarAsync(connection, "PRAGMA journal_mode = WAL;");
            }
        }

        _database.Configure(password, _database.IsFileLocked);
        DeleteBackups();
    }

    public void LockFile() => _database.Configure(_database.Password, isFileLocked: true);

    /// <summary>
    /// The copies made before schema upgrades keep the encryption the file had then: plain ones
    /// would keep the data readable, encrypted ones would need a password that may be forgotten.
    /// </summary>
    private void DeleteBackups()
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_database.FilePath))!;
        var name = Path.GetFileName(_database.FilePath);

        foreach (var pattern in new[] { name + ".v*.bak", name + ".ef-backup*" })
        {
            foreach (var backup in Directory.EnumerateFiles(directory, pattern))
                File.Delete(backup);
        }
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
