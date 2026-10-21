using Client.Application.Ports.Repositories;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class MutedChatsRepository : IMutedChatsRepository
{
    private readonly SqliteDatabase _database;

    public MutedChatsRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<int>> GetAsync(string coordinatorInstanceId)
    {
        await using var connection = await _database.OpenAsync();

        var chatIds = await connection.QueryAsync<int>(
            "SELECT ChatId FROM MutedChats WHERE CoordinatorInstanceId = @coordinatorInstanceId ORDER BY ChatId",
            new { coordinatorInstanceId });
        return chatIds.AsList();
    }

    public async Task SetAsync(string coordinatorInstanceId, int chatId, bool isMuted)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync(isMuted
            ? "INSERT INTO MutedChats (CoordinatorInstanceId, ChatId) VALUES (@coordinatorInstanceId, @chatId) ON CONFLICT DO NOTHING"
            : "DELETE FROM MutedChats WHERE CoordinatorInstanceId = @coordinatorInstanceId AND ChatId = @chatId",
            new { coordinatorInstanceId, chatId });
    }
}
