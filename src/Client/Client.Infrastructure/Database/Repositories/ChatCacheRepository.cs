using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Client.Application.Ports.Repositories;
using Client.Core.Chat;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class ChatCacheRepository : IChatCacheRepository
{
    internal const int DefaultMaxMessages = 2000;

    public int MaxMessages { get; set; } = DefaultMaxMessages;

    private const string WhereChat = " WHERE CoordinatorInstanceId = @CoordinatorInstanceId AND ChatId = @ChatId";

    private readonly SqliteDatabase _database;

    public ChatCacheRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<CachedChatPage?> GetNewestAsync(ChatCacheKey chat, int take)
    {
        await using var connection = await _database.OpenAsync();

        return await GetPageAsync(connection, chat, int.MaxValue, take);
    }

    public async Task<CachedChatPage?> GetBeforeAsync(ChatCacheKey chat, int beforeMessageId, int take)
    {
        await using var connection = await _database.OpenAsync();

        return await GetPageAsync(connection, chat, beforeMessageId, take);
    }

    private static async Task<CachedChatPage?> GetPageAsync(DbConnection connection, ChatCacheKey chat,
        int beforeMessageId, int take)
    {
        var tail = await GetTailAsync(connection, chat, null);
        if (tail == null)
            return null;

        // One more than asked for tells whether the tail goes on below the page
        var payloads = (await connection.QueryAsync<string>(
                "SELECT Payload FROM ChatCacheMessages" + WhereChat +
                " AND MessageId < @beforeMessageId ORDER BY MessageId DESC LIMIT @limit",
                new { chat.CoordinatorInstanceId, chat.ChatId, beforeMessageId, limit = take + 1 }))
            .ToList();

        bool isTailExhausted = payloads.Count <= take;
        var messages = payloads.Take(take).Select(Deserialize).Reverse().ToList();

        return new CachedChatPage(messages, tail.Revision, tail.ReachesStart && isTailExhausted);
    }

    public async Task ReplaceAsync(ChatCacheKey chat, IReadOnlyList<ChatMessageModel> messages, long revision,
        bool reachesStart)
    {
        await using var connection = await _database.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        // The messages of the tail go with it
        await connection.ExecuteAsync("DELETE FROM ChatCaches" + WhereChat,
            new { chat.CoordinatorInstanceId, chat.ChatId }, transaction);

        await connection.ExecuteAsync("""
            INSERT INTO ChatCaches (CoordinatorInstanceId, ChatId, Revision, ReachesStart)
            VALUES (@CoordinatorInstanceId, @ChatId, @revision, @reachesStart)
            """, new { chat.CoordinatorInstanceId, chat.ChatId, revision, reachesStart }, transaction);

        foreach (var message in messages)
        {
            if (!message.IsDeleted)
                await InsertAsync(connection, transaction, chat, message);
        }

        await transaction.CommitAsync();
    }

    public async Task AddOlderAsync(ChatCacheKey chat, int beforeMessageId, IReadOnlyList<ChatMessageModel> messages,
        bool reachesStart)
    {
        await using var connection = await _database.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        if (await GetTailAsync(connection, chat, transaction) == null)
            return;

        var bounds = await connection.QueryFirstAsync<TailBoundsRow>(
            "SELECT MIN(MessageId) AS OldestId, COUNT(*) AS Count FROM ChatCacheMessages" + WhereChat,
            new { chat.CoordinatorInstanceId, chat.ChatId }, transaction);

        if (bounds.OldestId != beforeMessageId || bounds.Count + messages.Count > MaxMessages)
            return;

        foreach (var message in messages)
        {
            if (!message.IsDeleted && message.Id < beforeMessageId)
                await InsertAsync(connection, transaction, chat, message);
        }

        await connection.ExecuteAsync("UPDATE ChatCaches SET ReachesStart = @reachesStart" + WhereChat,
            new { chat.CoordinatorInstanceId, chat.ChatId, reachesStart }, transaction);

        await transaction.CommitAsync();
    }

    public async Task ApplyAsync(ChatCacheKey chat, IReadOnlyList<ChatMessageModel> changed,
        IReadOnlyList<int> deletedIds, long revision)
    {
        await using var connection = await _database.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var tail = await GetTailAsync(connection, chat, transaction);
        if (tail == null)
            return;

        var oldestId = await connection.ExecuteScalarAsync<int?>(
            "SELECT MIN(MessageId) FROM ChatCacheMessages" + WhereChat,
            new { chat.CoordinatorInstanceId, chat.ChatId }, transaction);

        foreach (var messageId in deletedIds)
        {
            await connection.ExecuteAsync(
                "DELETE FROM ChatCacheMessages" + WhereChat + " AND MessageId = @messageId",
                new { chat.CoordinatorInstanceId, chat.ChatId, messageId }, transaction);

            await UpdateQuotesAsync(connection, transaction, chat, messageId, null);
        }

        foreach (var message in changed)
        {
            var storedRevision = await connection.ExecuteScalarAsync<long?>(
                "SELECT Revision FROM ChatCacheMessages" + WhereChat + " AND MessageId = @Id",
                new { chat.CoordinatorInstanceId, chat.ChatId, message.Id }, transaction);

            // A notification can arrive after a page that already had a newer state
            if (storedRevision >= message.Revision)
                continue;

            if (storedRevision != null)
            {
                await connection.ExecuteAsync(
                    "UPDATE ChatCacheMessages SET Revision = @Revision, ReplyToMessageId = @replyToMessageId, Payload = @payload" +
                    WhereChat + " AND MessageId = @Id",
                    new
                    {
                        chat.CoordinatorInstanceId,
                        chat.ChatId,
                        message.Id,
                        message.Revision,
                        replyToMessageId = message.ReplyContent?.MessageId,
                        payload = Serialize(message)
                    }, transaction);
            }
            // Only what continues the tail is added; older history comes with its page
            else if (tail.ReachesStart || oldestId == null || message.Id > oldestId)
            {
                await InsertAsync(connection, transaction, chat, message);
            }

            // The message itself may be older than the tail, while replies to it are in it
            await UpdateQuotesAsync(connection, transaction, chat, message.Id, message);
        }

        await connection.ExecuteAsync(
            "UPDATE ChatCaches SET Revision = MAX(Revision, @revision)" + WhereChat,
            new { chat.CoordinatorInstanceId, chat.ChatId, revision }, transaction);

        await TrimAsync(connection, transaction, chat);

        await transaction.CommitAsync();
    }

    public async Task<(int Messages, long Bytes)> GetUsageAsync()
    {
        await using var connection = await _database.OpenAsync();

        var usage = await connection.QuerySingleAsync<ChatCacheUsageRow>(
            "SELECT COUNT(*) AS Messages, COALESCE(SUM(LENGTH(CAST(Payload AS BLOB))), 0) AS Bytes FROM ChatCacheMessages");

        return (usage.Messages, usage.Bytes);
    }

    public async Task RemoveAllAsync()
    {
        await using var connection = await _database.OpenAsync();

        // The messages go with their chats
        await connection.ExecuteAsync("DELETE FROM ChatCaches");
    }

    public async Task RemoveAsync(ChatCacheKey chat)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("DELETE FROM ChatCaches" + WhereChat,
            new { chat.CoordinatorInstanceId, chat.ChatId });
    }

    public async Task RemoveExceptAsync(string coordinatorInstanceId, IReadOnlyCollection<int> existingChatIds)
    {
        await using var connection = await _database.OpenAsync();

        var cachedChatIds = await connection.QueryAsync<int>(
            "SELECT ChatId FROM ChatCaches WHERE CoordinatorInstanceId = @coordinatorInstanceId",
            new { coordinatorInstanceId });

        foreach (var chatId in cachedChatIds.Except(existingChatIds).ToList())
        {
            await connection.ExecuteAsync(
                "DELETE FROM ChatCaches WHERE CoordinatorInstanceId = @coordinatorInstanceId AND ChatId = @chatId",
                new { coordinatorInstanceId, chatId });
        }
    }

    private static Task<TailRow?> GetTailAsync(DbConnection connection, ChatCacheKey chat, DbTransaction? transaction) =>
        connection.QueryFirstOrDefaultAsync<TailRow>(
            "SELECT Revision, ReachesStart FROM ChatCaches" + WhereChat,
            new { chat.CoordinatorInstanceId, chat.ChatId }, transaction);

    private static Task InsertAsync(DbConnection connection, DbTransaction transaction, ChatCacheKey chat,
        ChatMessageModel message) =>
        connection.ExecuteAsync("""
            INSERT INTO ChatCacheMessages (CoordinatorInstanceId, ChatId, MessageId, Revision, ReplyToMessageId, Payload)
            VALUES (@CoordinatorInstanceId, @ChatId, @Id, @Revision, @replyToMessageId, @payload)
            ON CONFLICT (CoordinatorInstanceId, ChatId, MessageId) DO NOTHING
            """, new
            {
                chat.CoordinatorInstanceId,
                chat.ChatId,
                message.Id,
                message.Revision,
                replyToMessageId = message.ReplyContent?.MessageId,
                payload = Serialize(message)
            }, transaction);

    /// <summary>
    /// A quote is a copy of the quoted message made when its reply was read, so it is brought up to
    /// date here. A deleted message, passed as null, leaves its text in the quotes, marked as deleted.
    /// </summary>
    private static async Task UpdateQuotesAsync(DbConnection connection, DbTransaction transaction, ChatCacheKey chat,
        int quotedMessageId, ChatMessageModel? quoted)
    {
        var replies = await connection.QueryAsync<QuotingMessageRow>(
            "SELECT MessageId, Payload FROM ChatCacheMessages" + WhereChat + " AND ReplyToMessageId = @quotedMessageId",
            new { chat.CoordinatorInstanceId, chat.ChatId, quotedMessageId }, transaction);

        foreach (var reply in replies.ToList())
        {
            if (JsonNode.Parse(reply.Payload)?[nameof(ChatMessageModel.ReplyContent)] is not JsonObject quote)
                continue;

            quote[nameof(ChatReplyContent.IsDeleted)] = quoted == null;
            if (quoted != null)
            {
                quote[nameof(ChatReplyContent.TextContent)] = quoted.TextContent == null
                    ? null
                    : JsonSerializer.SerializeToNode(quoted.TextContent, ChatCacheJsonContext.Default.ChatTextContent);
            }

            await connection.ExecuteAsync(
                "UPDATE ChatCacheMessages SET Payload = @payload" + WhereChat + " AND MessageId = @MessageId",
                new { chat.CoordinatorInstanceId, chat.ChatId, reply.MessageId, payload = quote.Root.ToJsonString() },
                transaction);
        }
    }

    private async Task TrimAsync(DbConnection connection, DbTransaction transaction, ChatCacheKey chat)
    {
        // The subquery finds nothing while the tail is within the limit, and then nothing is deleted
        var removed = await connection.ExecuteAsync(
            "DELETE FROM ChatCacheMessages" + WhereChat + " AND MessageId <= (SELECT MessageId FROM ChatCacheMessages" +
            WhereChat + " ORDER BY MessageId DESC LIMIT 1 OFFSET @maxMessages)",
            new { chat.CoordinatorInstanceId, chat.ChatId, maxMessages = MaxMessages }, transaction);

        if (removed > 0)
        {
            await connection.ExecuteAsync("UPDATE ChatCaches SET ReachesStart = 0" + WhereChat,
                new { chat.CoordinatorInstanceId, chat.ChatId }, transaction);
        }
    }

    private static string Serialize(ChatMessageModel message) =>
        JsonSerializer.Serialize(message, ChatCacheJsonContext.Default.ChatMessageModel);

    private static ChatMessageModel Deserialize(string payload) =>
        JsonSerializer.Deserialize(payload, ChatCacheJsonContext.Default.ChatMessageModel)
        ?? throw new JsonException("A cached message is null");
}

internal sealed record TailRow(long Revision, bool ReachesStart);

internal sealed record TailBoundsRow(int? OldestId, int Count);

internal sealed record QuotingMessageRow(int MessageId, string Payload);

// Generated serializers: the app is published with NativeAOT, which has no reflection to fall back on
[JsonSerializable(typeof(ChatMessageModel))]
internal sealed partial class ChatCacheJsonContext : JsonSerializerContext;

internal sealed record ChatCacheUsageRow(int Messages, long Bytes);
