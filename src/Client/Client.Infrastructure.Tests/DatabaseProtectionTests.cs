using Xunit;
using Client.Application.Ports;
using Client.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Client.Infrastructure.Tests;

public class DatabaseProtectionTests
{
    [Fact]
    public async Task ANewDatabaseIsPlain()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();

        Assert.False(new DatabaseProtection(database.Database).IsEncrypted);
    }

    [Fact]
    public async Task EncryptedFileKeepsTheDataUnreadable()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        await database.ExecuteAsync("INSERT INTO DismissedPrompts (PromptKey) VALUES ('secret-marker')");
        var protection = new DatabaseProtection(database.Database);

        await protection.ChangePasswordAsync("pass'word");

        Assert.True(protection.IsEncrypted);
        Assert.Equal("secret-marker", await database.ScalarAsync("SELECT PromptKey FROM DismissedPrompts"));
        Assert.Equal("wal", await database.ScalarAsync("PRAGMA journal_mode"));

        SqliteConnection.ClearAllPools();
        Assert.DoesNotContain("secret-marker", await File.ReadAllTextAsync(database.FilePath));
    }

    [Fact]
    public async Task OpensOnlyWithItsPassword()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        await new DatabaseProtection(database.Database).ChangePasswordAsync("right");

        // As the next start sees it: the file without a password
        var reopened = new SqliteDatabase(database.FilePath);
        var protection = new DatabaseProtection(reopened);

        Assert.True(protection.IsEncrypted);
        Assert.False(await protection.UnlockAsync("wrong"));
        Assert.Null(reopened.Password);
        Assert.True(await protection.UnlockAsync("right"));

        var state = await new DatabaseInitializer(reopened, NullLogger<DatabaseInitializer>.Instance).VerifyDatabaseAsync();
        Assert.Equal(DatabaseState.NoError, state);
    }

    [Fact]
    public async Task AnEncryptedFileWithoutItsPasswordLooksCorrupted()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        await new DatabaseProtection(database.Database).ChangePasswordAsync("right");

        var state = await new DatabaseInitializer(new SqliteDatabase(database.FilePath),
            NullLogger<DatabaseInitializer>.Instance).VerifyDatabaseAsync();

        Assert.Equal(DatabaseState.Corrupted, state);
    }

    [Fact]
    public async Task ChangesThePasswordAndDecrypts()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var protection = new DatabaseProtection(database.Database);

        await protection.ChangePasswordAsync("first");
        await protection.ChangePasswordAsync("second");

        var reopened = new DatabaseProtection(new SqliteDatabase(database.FilePath));
        Assert.False(await reopened.UnlockAsync("first"));
        Assert.True(await reopened.UnlockAsync("second"));

        await protection.ChangePasswordAsync(null);

        Assert.False(protection.IsEncrypted);
        Assert.Null(database.Database.Password);
        Assert.Equal(1L, await database.ScalarAsync("SELECT COUNT(*) FROM AudioSettings"));
    }

    [Fact]
    public async Task DeletesTheBackupsOfTheOldEncryption()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var backup = database.FilePath + ".v3.bak";
        await File.WriteAllTextAsync(backup, "plain copy");

        await new DatabaseProtection(database.Database).ChangePasswordAsync("password");

        Assert.False(File.Exists(backup));
    }

    [Fact]
    public async Task ALockedFileServesManyConnections()
    {
        Assert.SkipUnless(SqliteDatabase.CanLockFile, "The file cannot be locked on this system");

        await using var database = await TestDatabase.CreateMigratedAsync();
        var protection = new DatabaseProtection(database.Database);
        await protection.ChangePasswordAsync("password");

        protection.LockFile();

        await using var first = await database.Database.OpenAsync();
        await using var second = await database.Database.OpenAsync();

        await using (var write = second.CreateCommand())
        {
            write.CommandText = "INSERT INTO DismissedPrompts (PromptKey) VALUES ('locked')";
            await write.ExecuteNonQueryAsync();
        }

        await using var read = first.CreateCommand();
        read.CommandText = "SELECT COUNT(*) FROM DismissedPrompts WHERE PromptKey = 'locked'";
        Assert.Equal(1L, await read.ExecuteScalarAsync());
        Assert.True(database.Database.IsFileLocked);
    }
}
