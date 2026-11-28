using Client.Database.Sqlite.Repositories;
using Xunit;

namespace Client.Database.Sqlite.Tests;

[Collection(DatabaseTestCollection.Name)]
public class ChatDraftsRepositoryTests
{
    [Fact]
    public async Task DraftsPersistAndAreIsolatedByCoordinator()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatDraftsRepository(database.Database);
        Assert.Empty(await repository.GetAllAsync("server-a"));

        const string text = "  первая строка\nвторая <@7> ";
        await repository.SetAsync("server-a", 7, text);
        await repository.SetAsync("server-a", 8, "old");
        await repository.SetAsync("server-a", 8, "new");
        await repository.SetAsync("server-b", 7, "another server");

        var reopened = new ChatDraftsRepository(new SqliteDatabase(database.FilePath));
        var drafts = await reopened.GetAllAsync("server-a");
        Assert.Equal(2, drafts.Count);
        Assert.Equal(text, drafts[7]);
        Assert.Equal("new", drafts[8]);
        Assert.Equal("another server", (await reopened.GetAllAsync("server-b"))[7]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n\t ")]
    public async Task AnEmptyDraftRemovesTheEntry(string? text)
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatDraftsRepository(database.Database);
        await repository.SetAsync("server-a", 7, "text");

        await repository.SetAsync("server-a", 7, text);

        Assert.Empty(await repository.GetAllAsync("server-a"));
    }
}
