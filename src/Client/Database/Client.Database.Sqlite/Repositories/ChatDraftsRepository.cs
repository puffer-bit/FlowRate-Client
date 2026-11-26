using Client.Application.Ports.Repositories;
using Dapper;

namespace Client.Database.Sqlite.Repositories;

internal sealed class ChatDraftsRepository(SqliteDatabase database) : IChatDraftsRepository
{
    public async Task<IReadOnlyDictionary<int, string>> GetAllAsync(string coordinatorInstanceId)
    {
        await using var connection = await database.OpenAsync();
        var rows = await connection.QueryAsync<ChatDraftRow>("""
            SELECT ChatId, Text FROM ChatDrafts WHERE CoordinatorInstanceId = @coordinatorInstanceId
            """, new { coordinatorInstanceId });
        return rows.ToDictionary(row => row.ChatId, row => row.Text);
    }

    public async Task SetAsync(string coordinatorInstanceId, int chatId, string? text)
    {
        await using var connection = await database.OpenAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            await connection.ExecuteAsync("""
                DELETE FROM ChatDrafts
                WHERE CoordinatorInstanceId = @coordinatorInstanceId AND ChatId = @chatId
                """, new { coordinatorInstanceId, chatId });
            return;
        }

        await connection.ExecuteAsync("""
            INSERT INTO ChatDrafts (CoordinatorInstanceId, ChatId, Text)
            VALUES (@coordinatorInstanceId, @chatId, @text)
            ON CONFLICT (CoordinatorInstanceId, ChatId) DO UPDATE SET Text = excluded.Text
            """, new { coordinatorInstanceId, chatId, text });
    }
}

internal sealed record ChatDraftRow(int ChatId, string Text);
