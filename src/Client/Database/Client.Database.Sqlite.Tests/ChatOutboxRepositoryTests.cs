using Client.Application.Ports.Repositories;
using Client.Database.Sqlite.Repositories;
using Xunit;

namespace Client.Database.Sqlite.Tests;

[Collection(DatabaseTestCollection.Name)]
public class ChatOutboxRepositoryTests
{
    [Fact]
    public async Task MessagesPersistWithTheirFiles_OldestFirst_AndPerCoordinator()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatOutboxRepository(database.Database);
        Assert.Empty(await repository.GetAllAsync("server-a"));

        var older = Message("server-a", 7, DateTimeOffset.FromUnixTimeMilliseconds(1_000), "first",
            new StoredOutgoingFile("cat.png", "/tmp/cat.png", 1234, true, true, false, null),
            new StoredOutgoingFile("clip.mp4", null, 5678, false, true, true, 720));
        var newer = Message("server-a", 8, DateTimeOffset.FromUnixTimeMilliseconds(2_000), null) with
        {
            ReplyToMessageId = 42, ReplySenderUserId = 3, ReplyText = "quoted", ReplyHasAttachments = true
        };
        await repository.SaveAsync(newer);
        await repository.SaveAsync(older);
        await repository.SaveAsync(Message("server-b", 7, DateTimeOffset.FromUnixTimeMilliseconds(500), "another server"));

        var stored = await new ChatOutboxRepository(new SqliteDatabase(database.FilePath)).GetAllAsync("server-a");

        Assert.Equal(new[] { older.ClientMessageId, newer.ClientMessageId }, stored.Select(message => message.ClientMessageId));
        Assert.Equal(older.Files, stored[0].Files);
        Assert.Equal("first", stored[0].Text);
        Assert.Equal(older.CreatedAt, stored[0].CreatedAt);
        Assert.Empty(stored[1].Files);
        Assert.Equal((42, 3, "quoted", true),
            (stored[1].ReplyToMessageId, stored[1].ReplySenderUserId, stored[1].ReplyText, stored[1].ReplyHasAttachments));
    }

    [Fact]
    public async Task SavingAgainReplacesTheFiles_AndDeletingForgetsTheMessage()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatOutboxRepository(database.Database);
        var message = Message("server-a", 7, DateTimeOffset.UtcNow, "text",
            new StoredOutgoingFile("a.txt", "/tmp/a.txt", 1, false, false, false, null),
            new StoredOutgoingFile("b.txt", "/tmp/b.txt", 2, false, false, false, null));
        await repository.SaveAsync(message);

        await repository.SaveAsync(message with { Files = [message.Files[1]] });
        Assert.Equal(new[] { message.Files[1] }, Assert.Single(await repository.GetAllAsync("server-a")).Files);

        await repository.DeleteAsync(message.ClientMessageId);
        await repository.DeleteAsync(Guid.NewGuid());
        Assert.Empty(await repository.GetAllAsync("server-a"));
    }

    private static StoredOutgoingMessage Message(string coordinator, int chatId, DateTimeOffset createdAt, string? text,
        params StoredOutgoingFile[] files) =>
        new(Guid.NewGuid(), coordinator, chatId, DateTimeOffset.FromUnixTimeMilliseconds(createdAt.ToUnixTimeMilliseconds()),
            text, false, null, null, null, false, files);
}
