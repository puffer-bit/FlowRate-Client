using Xunit;
using Client.Application.Ports;
using Client.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Client.Infrastructure.Tests;

public class DatabaseInitializerTests
{
    [Fact]
    public async Task CreatesTheSchemaInANewFile()
    {
        await using var database = TestDatabase.CreateEmpty();

        var state = await new DatabaseInitializer(database.Database, NullLogger<DatabaseInitializer>.Instance).VerifyDatabaseAsync();

        Assert.Equal(DatabaseState.NoError, state);
        Assert.Equal(11L, await database.ScalarAsync("PRAGMA user_version"));
        Assert.Equal("wal", await database.ScalarAsync("PRAGMA journal_mode"));
        Assert.Equal(1L, await database.ScalarAsync("SELECT COUNT(*) FROM AudioSettings"));
    }

    [Fact]
    public async Task RunningAgainChangesNothing()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        await database.ExecuteAsync("UPDATE AudioSettings SET InputVolume = 1.5");

        var state = await new DatabaseInitializer(database.Database, NullLogger<DatabaseInitializer>.Instance).VerifyDatabaseAsync();

        Assert.Equal(DatabaseState.NoError, state);
        Assert.Equal(1.5, await database.ScalarAsync("SELECT InputVolume FROM AudioSettings"));
    }

    [Fact]
    public async Task MovesAnEntityFrameworkDatabaseAside()
    {
        await using var database = TestDatabase.CreateEmpty();
        await database.ExecuteAsync("CREATE TABLE __EFMigrationsHistory (MigrationId TEXT); CREATE TABLE Coordinators (Id TEXT);");
        SqliteConnection.ClearAllPools();

        var state = await new DatabaseInitializer(database.Database, NullLogger<DatabaseInitializer>.Instance).VerifyDatabaseAsync();

        Assert.Equal(DatabaseState.NoError, state);
        Assert.True(File.Exists(database.FilePath + ".ef-backup"));
        Assert.Null(await database.ScalarAsync("SELECT name FROM sqlite_master WHERE name = '__EFMigrationsHistory'"));
        Assert.Equal(11L, await database.ScalarAsync("PRAGMA user_version"));
    }

    [Fact]
    public async Task RefusesADatabaseOfANewerBuild()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        await database.ExecuteAsync("PRAGMA user_version = 99");

        var state = await new DatabaseInitializer(database.Database, NullLogger<DatabaseInitializer>.Instance).VerifyDatabaseAsync();

        Assert.Equal(DatabaseState.NewerThanApplication, state);
        Assert.Equal(99L, await database.ScalarAsync("PRAGMA user_version"));
    }

    [Fact]
    public async Task ReportsAFileThatIsNotADatabase()
    {
        await using var database = TestDatabase.CreateEmpty();
        await File.WriteAllTextAsync(database.FilePath, new string('x', 4096), TestContext.Current.CancellationToken);

        var state = await new DatabaseInitializer(database.Database, NullLogger<DatabaseInitializer>.Instance).VerifyDatabaseAsync();

        Assert.Equal(DatabaseState.Corrupted, state);
    }
}
