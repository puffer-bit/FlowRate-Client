using Client.Application.Ports;
using Client.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Client.Infrastructure.Tests;

/// <summary>
/// A database file of its own in a temporary directory, removed with everything SQLite put next to it.
/// </summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _directory;

    private TestDatabase(string directory)
    {
        _directory = directory;
        Database = new SqliteDatabase(Path.Combine(directory, "client.db"));
    }

    public SqliteDatabase Database { get; }

    public string FilePath => Database.FilePath;

    /// <summary>
    /// An empty directory: the file does not exist until the first connection.
    /// </summary>
    public static TestDatabase CreateEmpty() =>
        new(Directory.CreateTempSubdirectory("flowrate-db-").FullName);

    /// <summary>
    /// A database migrated to the current schema.
    /// </summary>
    public static async Task<TestDatabase> CreateMigratedAsync()
    {
        var database = CreateEmpty();
        var state = await new DatabaseInitializer(database.Database, NullLogger<DatabaseInitializer>.Instance).VerifyDatabaseAsync();

        if (state != DatabaseState.NoError)
            throw new InvalidOperationException($"The test database failed to initialize: {state}");

        return database;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = await Database.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = await Database.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    public ValueTask DisposeAsync()
    {
        // Idle pooled connections keep the file open
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Left to the system cleanup of the temporary directory
        }

        return ValueTask.CompletedTask;
    }
}
