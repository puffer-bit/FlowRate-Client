using Client.Infrastructure.Database.Repositories;
using Xunit;

namespace Client.Infrastructure.Tests;

[Collection(DatabaseTestCollection.Name)]
public class UserNotesRepositoryTests
{
    [Fact]
    public async Task NotesPersistAndAreIsolatedByCoordinatorAndUser()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new UserNotesRepository(database.Database);
        Assert.Null(await repository.GetAsync("server-a", 7));

        const string text = "  Анна — дизайнер\nОбсудить макет 'профиля'  ";
        await repository.SetAsync("server-a", 7, text);
        await repository.SetAsync("server-b", 7, "Different person with the same ID");
        await repository.SetAsync("server-a", 8, "Another user");

        var reopened = new UserNotesRepository(new Client.Infrastructure.Database.SqliteDatabase(database.FilePath));
        Assert.Equal(text, await reopened.GetAsync("server-a", 7));
        Assert.Equal("Different person with the same ID", await reopened.GetAsync("server-b", 7));
        Assert.Equal("Another user", await reopened.GetAsync("server-a", 8));
        Assert.Null(await reopened.GetAsync("server-b", 8));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n\t ")]
    public async Task UpdatingAndRemovingANoteLeavesOtherNotesIntact(string emptyText)
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new UserNotesRepository(database.Database);
        await repository.SetAsync("server", 1, "Before");
        await repository.SetAsync("server", 2, "Keep");
        await repository.SetAsync("server", 1, "After");
        Assert.Equal("After", await repository.GetAsync("server", 1));
        Assert.Equal(2L, await database.ScalarAsync("SELECT COUNT(*) FROM UserNotes"));

        await repository.SetAsync("server", 1, emptyText);

        Assert.Null(await repository.GetAsync("server", 1));
        Assert.Equal("Keep", await repository.GetAsync("server", 2));
        Assert.Equal(1L, await database.ScalarAsync("SELECT COUNT(*) FROM UserNotes"));
    }

    [Fact]
    public async Task UpgradingAnExistingDatabaseAddsNotesWithoutChangingExistingSettings()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        await database.ExecuteAsync("DROP TABLE UserNotes; UPDATE AudioSettings SET InputVolume = 1.5; PRAGMA user_version = 25;");
        var state = await new Client.Infrastructure.Database.DatabaseInitializer(database.Database,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Client.Infrastructure.Database.DatabaseInitializer>.Instance)
            .VerifyDatabaseAsync();

        Assert.Equal(Client.Application.Ports.DatabaseState.NoError, state);
        Assert.Equal(27L, await database.ScalarAsync("PRAGMA user_version"));
        Assert.Equal(1.5, await database.ScalarAsync("SELECT InputVolume FROM AudioSettings"));
        var repository = new UserNotesRepository(database.Database);
        await repository.SetAsync("server", 7, "New note");
        Assert.Equal("New note", await repository.GetAsync("server", 7));
    }
}
