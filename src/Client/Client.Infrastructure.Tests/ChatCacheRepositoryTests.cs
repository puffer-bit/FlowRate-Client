using Xunit;
using Client.Application.Ports.Repositories;
using Client.Core.Chat;
using Client.Infrastructure.Database.Repositories;

namespace Client.Infrastructure.Tests;

public class ChatCacheRepositoryTests
{
    private static readonly ChatCacheKey Chat = new("instance", 7);
    private static readonly DateTime SentTime = new(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc);

    private static ChatMessageModel Message(int id, long revision, string text = "text", int? replyTo = null,
        bool isDeleted = false) => new()
    {
        Id = id,
        ChatId = Chat.ChatId,
        Revision = revision,
        SenderUserId = 3,
        SentTime = SentTime.AddMinutes(id),
        TextContent = isDeleted ? null : new ChatTextContent { Text = text },
        ReplyContent = replyTo is { } targetId
            ? new ChatReplyContent
            {
                MessageId = targetId,
                SenderUserId = 4,
                TextContent = new ChatTextContent { Text = "quoted" }
            }
            : null,
        Images = [],
        Video = [],
        Files = [],
        Reactions = [new ChatReaction { UserId = 5, Emoji = "+" }],
        UsersIds = [],
        IsDeleted = isDeleted
    };

    private static List<ChatMessageModel> Messages(int fromId, int toId) =>
        Enumerable.Range(fromId, toId - fromId + 1).Select(id => Message(id, id)).ToList();

    private static int[] Ids(CachedChatPage? page) => page!.Messages.Select(message => message.Id).ToArray();

    [Fact]
    public async Task ChatWithoutATailHasNoPages()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatCacheRepository(database.Database);

        Assert.Null(await repository.GetNewestAsync(Chat, 10));
        Assert.Null(await repository.GetBeforeAsync(Chat, 5, 10));
    }

    [Fact]
    public async Task MessageRoundTrips()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatCacheRepository(database.Database);

        await repository.ReplaceAsync(Chat, [Message(1, 4, "первое\nсообщение", replyTo: 9)], 4, true);
        var page = await repository.GetNewestAsync(Chat, 10);

        var message = Assert.Single(page!.Messages);
        Assert.Equal(4, page.Revision);
        Assert.True(page.ReachesStart);
        Assert.Equal(1, message.Id);
        Assert.Equal(Chat.ChatId, message.ChatId);
        Assert.Equal(4, message.Revision);
        Assert.Equal(3, message.SenderUserId);
        Assert.Equal(SentTime.AddMinutes(1), message.SentTime);
        Assert.Equal(DateTimeKind.Utc, message.SentTime.Kind);
        Assert.Equal("первое\nсообщение", message.TextContent?.Text);
        Assert.Equal(9, message.ReplyContent?.MessageId);
        Assert.Equal(4, message.ReplyContent?.SenderUserId);
        Assert.Equal("quoted", message.ReplyContent?.TextContent?.Text);
        Assert.Equal("+", Assert.Single(message.Reactions).Emoji);
    }

    [Fact]
    public async Task PagesGoFromNewestToOldest()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatCacheRepository(database.Database);
        await repository.ReplaceAsync(Chat, Messages(1, 5), 5, true);

        var newest = await repository.GetNewestAsync(Chat, 2);
        var middle = await repository.GetBeforeAsync(Chat, 4, 2);
        var oldest = await repository.GetBeforeAsync(Chat, 2, 2);

        Assert.Equal([4, 5], Ids(newest));
        Assert.False(newest!.ReachesStart);
        Assert.Equal([2, 3], Ids(middle));
        Assert.False(middle!.ReachesStart);
        Assert.Equal([1], Ids(oldest));
        Assert.True(oldest!.ReachesStart);
    }

    [Fact]
    public async Task ExhaustedTailThatDoesNotReachTheStartSaysSo()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatCacheRepository(database.Database);
        await repository.ReplaceAsync(Chat, Messages(4, 5), 5, false);

        var page = await repository.GetBeforeAsync(Chat, 4, 2);

        Assert.Empty(page!.Messages);
        Assert.False(page.ReachesStart);
    }

    [Fact]
    public async Task OlderPageExtendsTheTailOnlyWhenItContinuesIt()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatCacheRepository(database.Database);
        await repository.ReplaceAsync(Chat, Messages(6, 8), 8, false);

        await repository.AddOlderAsync(Chat, 4, Messages(1, 3), true);

        var untouched = await repository.GetNewestAsync(Chat, 10);
        Assert.Equal([6, 7, 8], Ids(untouched));
        Assert.False(untouched!.ReachesStart);

        await repository.AddOlderAsync(Chat, 6, Messages(3, 5), true);
        var page = await repository.GetNewestAsync(Chat, 10);

        Assert.Equal([3, 4, 5, 6, 7, 8], Ids(page));
        Assert.True(page!.ReachesStart);
        Assert.Equal(8, page.Revision);
    }

    [Fact]
    public async Task ReplacingDropsTheOldTail()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatCacheRepository(database.Database);
        await repository.ReplaceAsync(Chat, Messages(1, 5), 5, true);

        await repository.ReplaceAsync(Chat, Messages(900, 901), 950, false);
        var page = await repository.GetNewestAsync(Chat, 10);

        Assert.Equal([900, 901], Ids(page));
        Assert.Equal(950, page!.Revision);
        Assert.False(page.ReachesStart);
    }

    [Fact]
    public async Task ChangesAreAppliedById()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatCacheRepository(database.Database);
        await repository.ReplaceAsync(Chat, Messages(4, 6), 6, false);

        await repository.ApplyAsync(Chat, [Message(5, 7, "edited"), Message(7, 8, "new"), Message(2, 9, "old")], [4], 9);
        var page = await repository.GetNewestAsync(Chat, 10);

        Assert.Equal([5, 6, 7], Ids(page));
        Assert.Equal("edited", page!.Messages[0].TextContent?.Text);
        Assert.Equal(7, page.Messages[0].Revision);
        Assert.Equal(9, page.Revision);
    }

    [Fact]
    public async Task OlderStateDoesNotOverwriteANewerOneAndTheRevisionDoesNotGoBack()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatCacheRepository(database.Database);
        await repository.ReplaceAsync(Chat, [Message(1, 10, "newer")], 10, true);

        await repository.ApplyAsync(Chat, [Message(1, 6, "older")], [], 6);
        var page = await repository.GetNewestAsync(Chat, 10);

        Assert.Equal("newer", Assert.Single(page!.Messages).TextContent?.Text);
        Assert.Equal(10, page.Revision);
    }

    [Fact]
    public async Task TailThatReachesTheStartTakesAnyMessage()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatCacheRepository(database.Database);
        await repository.ReplaceAsync(Chat, Messages(4, 5), 5, true);

        await repository.ApplyAsync(Chat, [Message(2, 6)], [], 6);

        var page = await repository.GetNewestAsync(Chat, 10);

        Assert.Equal([2, 4, 5], Ids(page));
    }

    [Fact]
    public async Task ChangesWithoutATailAreIgnored()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatCacheRepository(database.Database);

        await repository.ApplyAsync(Chat, [Message(1, 1)], [], 1);
        await repository.AddOlderAsync(Chat, 5, Messages(1, 4), true);

        Assert.Null(await repository.GetNewestAsync(Chat, 10));
    }

    [Fact]
    public async Task QuotesFollowTheQuotedMessageEvenBelowTheTail()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatCacheRepository(database.Database);
        await repository.ReplaceAsync(Chat, [Message(10, 10, replyTo: 2), Message(11, 11, replyTo: 10)], 11, false);

        await repository.ApplyAsync(Chat, [Message(2, 12, "quoted anew")], [10], 13);
        var page = await repository.GetNewestAsync(Chat, 10);

        var reply = Assert.Single(page!.Messages);
        Assert.Equal(11, reply.Id);
        Assert.Equal("text", reply.TextContent?.Text);
        Assert.True(reply.ReplyContent?.IsDeleted);
        Assert.Equal("quoted", reply.ReplyContent?.TextContent?.Text);

        await repository.ReplaceAsync(Chat, [Message(10, 10, replyTo: 2)], 11, false);
        await repository.ApplyAsync(Chat, [Message(2, 12, "quoted anew")], [], 12);
        reply = Assert.Single((await repository.GetNewestAsync(Chat, 10))!.Messages);

        Assert.False(reply.ReplyContent?.IsDeleted);
        Assert.Equal("quoted anew", reply.ReplyContent?.TextContent?.Text);
        Assert.Equal(4, reply.ReplyContent?.SenderUserId);
    }

    [Fact]
    public async Task TailIsCutDownToTheLimit()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatCacheRepository(database.Database);
        int limit = ChatCacheRepository.DefaultMaxMessages;
        await repository.ReplaceAsync(Chat, Messages(1, limit), limit, true);

        await repository.AddOlderAsync(Chat, 1, [Message(0, 0)], true);
        await repository.ApplyAsync(Chat, [Message(limit + 1, limit + 1), Message(limit + 2, limit + 2)], [], limit + 2);
        var oldest = await repository.GetBeforeAsync(Chat, 4, 10);

        Assert.Equal([3], Ids(oldest));
        Assert.False(oldest!.ReachesStart);
        Assert.Equal((long)limit, await database.ScalarAsync("SELECT COUNT(*) FROM ChatCacheMessages"));
    }

    [Fact]
    public async Task TailLimit_CanBeLowered()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatCacheRepository(database.Database) { MaxMessages = 5 };
        await repository.ReplaceAsync(Chat, Messages(1, 5), 5, true);

        await repository.ApplyAsync(Chat, [Message(6, 6), Message(7, 7)], [], 7);

        var kept = await repository.GetNewestAsync(Chat, 10);
        Assert.Equal([3, 4, 5, 6, 7], Ids(kept));
    }

    [Fact]
    public async Task UsageCountsEveryChat_AndRemovingAllEmptiesIt()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatCacheRepository(database.Database);
        Assert.Equal((0, 0L), await repository.GetUsageAsync());

        await repository.ReplaceAsync(Chat, Messages(1, 3), 3, true);
        await repository.ReplaceAsync(new ChatCacheKey("other", 7), Messages(1, 2), 2, true);

        var (messages, bytes) = await repository.GetUsageAsync();
        Assert.Equal(5, messages);
        Assert.Equal(Convert.ToInt64(await database.ScalarAsync("SELECT SUM(LENGTH(CAST(Payload AS BLOB))) FROM ChatCacheMessages")), bytes);
        Assert.True(bytes > 0);

        await repository.RemoveAllAsync();

        Assert.Equal((0, 0L), await repository.GetUsageAsync());
        Assert.Null(await repository.GetNewestAsync(Chat, 10));
        Assert.Equal(0L, await database.ScalarAsync("SELECT COUNT(*) FROM ChatCaches"));
    }

    [Fact]
    public async Task RemovingChats()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new ChatCacheRepository(database.Database);
        var other = new ChatCacheKey("other", 7);
        foreach (var chat in new[] { Chat, Chat with { ChatId = 8 }, Chat with { ChatId = 9 }, other })
            await repository.ReplaceAsync(chat, Messages(1, 2), 2, true);

        await repository.RemoveAsync(Chat);
        await repository.RemoveExceptAsync(Chat.CoordinatorInstanceId, [9]);

        Assert.Null(await repository.GetNewestAsync(Chat, 10));
        Assert.Null(await repository.GetNewestAsync(Chat with { ChatId = 8 }, 10));
        Assert.NotNull(await repository.GetNewestAsync(Chat with { ChatId = 9 }, 10));
        Assert.NotNull(await repository.GetNewestAsync(other, 10));
        Assert.Equal(4L, await database.ScalarAsync("SELECT COUNT(*) FROM ChatCacheMessages"));
    }
}
