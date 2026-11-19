using Client.Application.Ports.Repositories;
using Client.Core.Chat;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class ChatReadStateRepository(SqliteDatabase database) : IChatReadStateRepository
{
    public async Task<IReadOnlyList<ChatReadState>> GetAsync(string coordinatorInstanceId, int userId)
    {
        await using var connection = await database.OpenAsync();
        var rows = await connection.QueryAsync<ChatReadState>("""
            SELECT ChatId, LastReadMessageId, 0 AS UnreadCount, 0 AS MentionCount FROM LocalChatReads
            WHERE CoordinatorInstanceId = @coordinatorInstanceId AND UserId = @userId
            """, new { coordinatorInstanceId, userId });
        return rows.AsList();
    }

    public async Task SetAsync(string coordinatorInstanceId, int userId, ChatReadState state)
    {
        await using var connection = await database.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO LocalChatReads (CoordinatorInstanceId, UserId, ChatId, LastReadMessageId)
            VALUES (@coordinatorInstanceId, @userId, @ChatId, @LastReadMessageId)
            ON CONFLICT (CoordinatorInstanceId, UserId, ChatId) DO UPDATE SET
                LastReadMessageId = MAX(LastReadMessageId, excluded.LastReadMessageId)
            """, new { coordinatorInstanceId, userId, state.ChatId, state.LastReadMessageId });
    }
}
