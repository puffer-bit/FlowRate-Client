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
        Assert.Equal(27L, await database.ScalarAsync("PRAGMA user_version"));
        Assert.Equal("wal", await database.ScalarAsync("PRAGMA journal_mode"));
        Assert.Equal(1L, await database.ScalarAsync("SELECT COUNT(*) FROM AudioSettings"));
    }

    [Fact]
    public async Task ExistingAudioSettingsGainChatVolumeWithoutChangingOtherVolumes()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        await database.ExecuteAsync("""
            ALTER TABLE AudioSettings DROP COLUMN ChatAudioVolume;
            ALTER TABLE AudioSettings DROP COLUMN ChatVideoVolume;
            UPDATE AudioSettings SET InputVolume = 1.5, SoundsVolume = 0.4;
            PRAGMA user_version = 23;
            """);

        var state = await new DatabaseInitializer(database.Database, NullLogger<DatabaseInitializer>.Instance).VerifyDatabaseAsync();

        Assert.Equal(DatabaseState.NoError, state);
        Assert.Equal(27L, await database.ScalarAsync("PRAGMA user_version"));
        Assert.Equal(1.0, await database.ScalarAsync("SELECT ChatAudioVolume FROM AudioSettings"));
        Assert.Equal(1.5, await database.ScalarAsync("SELECT InputVolume FROM AudioSettings"));
        Assert.Equal(0.4, await database.ScalarAsync("SELECT SoundsVolume FROM AudioSettings"));
    }

    [Fact]
    public async Task ExistingAudioSettingsGainVideoVolumeWithoutChangingAudioVolume()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        await database.ExecuteAsync("""
            ALTER TABLE AudioSettings DROP COLUMN ChatVideoVolume;
            UPDATE AudioSettings SET ChatAudioVolume = 0.25;
            PRAGMA user_version = 24;
            """);

        var state = await new DatabaseInitializer(database.Database, NullLogger<DatabaseInitializer>.Instance).VerifyDatabaseAsync();

        Assert.Equal(DatabaseState.NoError, state);
        Assert.Equal(27L, await database.ScalarAsync("PRAGMA user_version"));
        Assert.Equal(1.0, await database.ScalarAsync("SELECT ChatVideoVolume FROM AudioSettings"));
        Assert.Equal(0.25, await database.ScalarAsync("SELECT ChatAudioVolume FROM AudioSettings"));
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
        Assert.Equal(27L, await database.ScalarAsync("PRAGMA user_version"));
    }

    [Fact]
    public async Task FrameCounterBecomesTheStatisticsOverTheScreens()
    {
        // A database of 021, where the frame counter was a column of the debug settings
        await using var database = await TestDatabase.CreateMigratedAsync();
        await database.ExecuteAsync("ALTER TABLE SavedCoordinators DROP COLUMN SaveOrder; ALTER TABLE AudioSettings DROP COLUMN ChatAudioVolume; ALTER TABLE AudioSettings DROP COLUMN ChatVideoVolume");
        await database.ExecuteAsync(string.Join(";",
            new[]
            {
                "MediaPerfOverlay", "IsMediaPerfExpanded", "MediaPerfSections", "MediaPerfIntervalMs",
                "ShowMediaPerfGraphs", "MediaPerfJournal", "MediaPerfSummarySeconds", "LogMediaPerfAnomalies"
            }.Select(column => $"ALTER TABLE DebugSettings DROP COLUMN {column}")));
        await database.ExecuteAsync(
            "ALTER TABLE DebugSettings ADD COLUMN ShowFrameStats INTEGER NOT NULL DEFAULT 0; " +
            "UPDATE DebugSettings SET ShowFrameStats = 1, LibWebRTCLogLevel = 3; PRAGMA user_version = 21");

        var state = await new DatabaseInitializer(database.Database, NullLogger<DatabaseInitializer>.Instance).VerifyDatabaseAsync();

        Assert.Equal(DatabaseState.NoError, state);
        Assert.Equal(27L, await database.ScalarAsync("PRAGMA user_version"));
        Assert.Equal(1L, await database.ScalarAsync("SELECT MediaPerfOverlay FROM DebugSettings"));
        Assert.Equal(31L, await database.ScalarAsync("SELECT MediaPerfSections FROM DebugSettings"));
        Assert.Equal(3L, await database.ScalarAsync("SELECT LibWebRTCLogLevel FROM DebugSettings"));
        Assert.Equal(0L, await database.ScalarAsync(
            "SELECT COUNT(*) FROM pragma_table_info('DebugSettings') WHERE name = 'ShowFrameStats'"));
    }

    [Fact]
    public async Task ExistingCoordinatorsGainSaveOrderWithoutLosingTheirIdentities()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        await database.ExecuteAsync("""
            ALTER TABLE SavedCoordinators DROP COLUMN SaveOrder;
            ALTER TABLE AudioSettings DROP COLUMN ChatAudioVolume;
            ALTER TABLE AudioSettings DROP COLUMN ChatVideoVolume;
            INSERT INTO SavedCoordinators (CoordinatorId, Address, Port)
                VALUES ('old', 'server.local', 5000), ('replacement', 'server.local', 5000);
            INSERT INTO SavedIdentities (CoordinatorId, UserId, UserIdentity, Username)
                VALUES ('old', 1, 'old-identity', 'old-user'),
                       ('replacement', 1, 'new-identity', 'new-user');
            PRAGMA user_version = 22;
            """);

        var state = await new DatabaseInitializer(database.Database, NullLogger<DatabaseInitializer>.Instance).VerifyDatabaseAsync();

        Assert.Equal(DatabaseState.NoError, state);
        Assert.Equal(27L, await database.ScalarAsync("PRAGMA user_version"));
        var repository = new Client.Infrastructure.Database.Repositories.AccountingSettingsRepository(database.Database);
        var coordinator = await repository.GetCoordinatorAsync("replacement");
        Assert.Equal("replacement", coordinator!.CoordinatorId);
        Assert.Equal("new-identity", Assert.Single(coordinator.Users).UserIdentity);
        Assert.Equal(2L, await database.ScalarAsync("SELECT COUNT(*) FROM SavedIdentities"));
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

    [Fact]
    public async Task ResetMovesTheFileAsideAndStartsANewOne()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        await database.ExecuteAsync("PRAGMA user_version = 99");
        var initializer = new DatabaseInitializer(database.Database, NullLogger<DatabaseInitializer>.Instance);

        var backupPath = initializer.ResetDatabase();

        Assert.True(File.Exists(backupPath));
        Assert.StartsWith(database.FilePath + ".broken-", backupPath);
        Assert.False(File.Exists(database.FilePath));

        Assert.Equal(DatabaseState.NoError, await initializer.VerifyDatabaseAsync());
        Assert.Equal(1L, await database.ScalarAsync("SELECT COUNT(*) FROM AudioSettings"));
    }

    [Fact]
    public async Task ResetForgetsThePasswordOfTheOldFile()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        database.Database.Configure("secret", isFileLocked: false);
        var initializer = new DatabaseInitializer(database.Database, NullLogger<DatabaseInitializer>.Instance);

        initializer.ResetDatabase();

        Assert.Null(database.Database.Password);
        Assert.Equal(DatabaseState.NoError, await initializer.VerifyDatabaseAsync());
        Assert.False(database.Database.IsFileEncrypted);
    }
}
