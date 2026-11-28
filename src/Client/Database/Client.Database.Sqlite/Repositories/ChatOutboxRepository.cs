using System.Globalization;
using Client.Application.Ports.Repositories;
using Dapper;

namespace Client.Database.Sqlite.Repositories;

internal sealed class ChatOutboxRepository(SqliteDatabase database) : IChatOutboxRepository
{
    public async Task<IReadOnlyList<StoredOutgoingMessage>> GetAllAsync(string coordinatorInstanceId)
    {
        await using var connection = await database.OpenAsync();
        var messages = (await connection.QueryAsync<MessageRow>("""
            SELECT ClientMessageId, ChatId, CreatedAt, Text, IsLinkPreviewDisabled,
                   ReplyToMessageId, ReplySenderUserId, ReplyText, ReplyHasAttachments
            FROM ChatOutbox
            WHERE CoordinatorInstanceId = @coordinatorInstanceId
            ORDER BY CreatedAt, ClientMessageId
            """, new { coordinatorInstanceId })).ToList();
        if (messages.Count == 0)
            return [];

        var files = (await connection.QueryAsync<FileRow>("""
                SELECT f.ClientMessageId, f.Position, f.Name, f.Path, f.Size, f.IsImage, f.IsCompressed,
                       f.IsMetadataStripped, f.VideoSide
                FROM ChatOutboxFiles f
                JOIN ChatOutbox m ON m.ClientMessageId = f.ClientMessageId
                WHERE m.CoordinatorInstanceId = @coordinatorInstanceId
                ORDER BY f.ClientMessageId, f.Position
                """, new { coordinatorInstanceId }))
            .GroupBy(file => file.ClientMessageId)
            .ToDictionary(group => group.Key, group => group
                .Select(file => new StoredOutgoingFile(file.Name, file.Path, file.Size, file.IsImage, file.IsCompressed,
                    file.IsMetadataStripped, file.VideoSide))
                .ToList());

        return messages.Select(row => new StoredOutgoingMessage(
                Guid.Parse(row.ClientMessageId), coordinatorInstanceId, row.ChatId,
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAt), row.Text, row.IsLinkPreviewDisabled,
                row.ReplyToMessageId, row.ReplySenderUserId, row.ReplyText, row.ReplyHasAttachments,
                files.GetValueOrDefault(row.ClientMessageId) ?? []))
            .ToList();
    }

    public async Task SaveAsync(StoredOutgoingMessage message)
    {
        var id = Key(message.ClientMessageId);
        await using var connection = await database.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await connection.ExecuteAsync("""
            INSERT INTO ChatOutbox (ClientMessageId, CoordinatorInstanceId, ChatId, CreatedAt, Text, IsLinkPreviewDisabled,
                                    ReplyToMessageId, ReplySenderUserId, ReplyText, ReplyHasAttachments)
            VALUES (@id, @CoordinatorInstanceId, @ChatId, @createdAt, @Text, @IsLinkPreviewDisabled,
                    @ReplyToMessageId, @ReplySenderUserId, @ReplyText, @ReplyHasAttachments)
            ON CONFLICT (ClientMessageId) DO UPDATE SET
                Text = excluded.Text, IsLinkPreviewDisabled = excluded.IsLinkPreviewDisabled,
                ReplyToMessageId = excluded.ReplyToMessageId, ReplySenderUserId = excluded.ReplySenderUserId,
                ReplyText = excluded.ReplyText, ReplyHasAttachments = excluded.ReplyHasAttachments
            """, new
            {
                id,
                message.CoordinatorInstanceId,
                message.ChatId,
                createdAt = message.CreatedAt.ToUnixTimeMilliseconds(),
                message.Text,
                message.IsLinkPreviewDisabled,
                message.ReplyToMessageId,
                message.ReplySenderUserId,
                message.ReplyText,
                message.ReplyHasAttachments
            }, transaction);

        await connection.ExecuteAsync("DELETE FROM ChatOutboxFiles WHERE ClientMessageId = @id", new { id }, transaction);
        for (var position = 0; position < message.Files.Count; position++)
        {
            var file = message.Files[position];
            await connection.ExecuteAsync("""
                INSERT INTO ChatOutboxFiles (ClientMessageId, Position, Name, Path, Size, IsImage, IsCompressed,
                                             IsMetadataStripped, VideoSide)
                VALUES (@id, @position, @Name, @Path, @Size, @IsImage, @IsCompressed, @IsMetadataStripped, @VideoSide)
                """, new
                {
                    id,
                    position,
                    file.Name,
                    file.Path,
                    file.Size,
                    file.IsImage,
                    file.IsCompressed,
                    file.IsMetadataStripped,
                    file.VideoSide
                }, transaction);
        }

        await transaction.CommitAsync();
    }

    public async Task DeleteAsync(Guid clientMessageId)
    {
        var id = Key(clientMessageId);
        await using var connection = await database.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync("DELETE FROM ChatOutboxFiles WHERE ClientMessageId = @id", new { id }, transaction);
        await connection.ExecuteAsync("DELETE FROM ChatOutbox WHERE ClientMessageId = @id", new { id }, transaction);
        await transaction.CommitAsync();
    }

    private static string Key(Guid clientMessageId) => clientMessageId.ToString("D", CultureInfo.InvariantCulture);

    // Dapper.AOT generates the row factories outside this class and must be able to access the types.
    internal sealed class MessageRow
    {
        public string ClientMessageId { get; init; } = "";
        public int ChatId { get; init; }
        public long CreatedAt { get; init; }
        public string? Text { get; init; }
        public bool IsLinkPreviewDisabled { get; init; }
        public int? ReplyToMessageId { get; init; }
        public int? ReplySenderUserId { get; init; }
        public string? ReplyText { get; init; }
        public bool ReplyHasAttachments { get; init; }
    }

    internal sealed class FileRow
    {
        public string ClientMessageId { get; init; } = "";
        public int Position { get; init; }
        public string Name { get; init; } = "";
        public string? Path { get; init; }
        public long Size { get; init; }
        public bool IsImage { get; init; }
        public bool IsCompressed { get; init; }
        public bool IsMetadataStripped { get; init; }
        public int? VideoSide { get; init; }
    }
}
