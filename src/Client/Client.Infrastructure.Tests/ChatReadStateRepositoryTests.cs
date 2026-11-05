using Client.Core.Chat;
using Client.Infrastructure.Database;
using Client.Infrastructure.Database.Repositories;
using Xunit;

namespace Client.Infrastructure.Tests;

[Collection(DatabaseTestCollection.Name)]
public class ChatReadStateRepositoryTests
{
    [Fact]
    public async Task UpgradeAddsLocalReadsWithoutChangingExistingData()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        await database.ExecuteAsync("""
            DROP TABLE LocalChatReads;
            DROP TABLE PushReadAccounts;
            UPDATE AudioSettings SET InputVolume = 1.5;
            PRAGMA user_version = 26;
            """);
        var result = await new DatabaseInitializer(database.Database,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseInitializer>.Instance).VerifyDatabaseAsync();
        Assert.Equal(Client.Application.Ports.DatabaseState.NoError, result);
        Assert.Equal(27L, await database.ScalarAsync("PRAGMA user_version"));
        Assert.Equal(1.5, await database.ScalarAsync("SELECT InputVolume FROM AudioSettings"));
        var repository = new ChatReadStateRepository(database.Database);
        await repository.SetAsync("server", 1, new ChatReadState(7, 20, 0, 0));
        Assert.Equal([new ChatReadState(7, 20, 0, 0)], await repository.GetAsync("server", 1));
    }

    [Fact]
    public async Task PositionsPersistAreIsolatedAndNeverMoveBackwards()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatReadStateRepository(database.Database);
        await repository.SetAsync("server", 1, new ChatReadState(7, 20, 2, 1));
        await repository.SetAsync("server", 1, new ChatReadState(7, 10, 0, 0));
        await repository.SetAsync("server", 2, new ChatReadState(7, 30, 0, 0));
        await repository.SetAsync("other", 1, new ChatReadState(7, 40, 0, 0));

        repository = new ChatReadStateRepository(new SqliteDatabase(database.FilePath));
        Assert.Equal([new ChatReadState(7, 20, 0, 0)], await repository.GetAsync("server", 1));
        Assert.Equal([new ChatReadState(7, 30, 0, 0)], await repository.GetAsync("server", 2));
        Assert.Equal([new ChatReadState(7, 40, 0, 0)], await repository.GetAsync("other", 1));
        Assert.Empty(await repository.GetAsync("other", 2));
    }
}
