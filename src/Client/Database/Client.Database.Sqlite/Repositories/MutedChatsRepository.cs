using Client.Application.Ports.Repositories;
using Dapper;

namespace Client.Database.Sqlite.Repositories;

internal sealed class MutedChatsRepository : IMutedChatsRepository
{
    // A mute that has ended is left in the table until the chat is muted or unmuted again
    private const string WhereMutedNow =
        " WHERE CoordinatorInstanceId = @coordinatorInstanceId AND (MutedUntil IS NULL OR MutedUntil > @now)";

    private readonly SqliteDatabase _database;
    private readonly TimeProvider _time;

    public MutedChatsRepository(SqliteDatabase database) : this(database, TimeProvider.System)
    {
    }

    internal MutedChatsRepository(SqliteDatabase database, TimeProvider time)
    {
        _database = database;
        _time = time;
    }

    public async Task<IReadOnlyList<int>> GetAsync(string coordinatorInstanceId)
    {
        await using var connection = await _database.OpenAsync();

        var chatIds = await connection.QueryAsync<int>(
            "SELECT ChatId FROM MutedChats" + WhereMutedNow + " ORDER BY ChatId",
            new { coordinatorInstanceId, now = Now() });
        return chatIds.AsList();
    }

    public async Task<IReadOnlyDictionary<int, DateTimeOffset?>> GetUntilAsync(string coordinatorInstanceId)
    {
        await using var connection = await _database.OpenAsync();

        var rows = await connection.QueryAsync<MutedChatRow>(
            "SELECT ChatId, MutedUntil FROM MutedChats" + WhereMutedNow,
            new { coordinatorInstanceId, now = Now() });
        return rows.ToDictionary(row => row.ChatId,
            row => row.MutedUntil is { } until ? DateTimeOffset.FromUnixTimeMilliseconds(until) : (DateTimeOffset?)null);
    }

    public async Task SetAsync(string coordinatorInstanceId, int chatId, bool isMuted, DateTimeOffset? until = null)
    {
        await using var connection = await _database.OpenAsync();

        var mutedUntil = until?.ToUnixTimeMilliseconds();
        await connection.ExecuteAsync(isMuted
            ? """
              INSERT INTO MutedChats (CoordinatorInstanceId, ChatId, MutedUntil) VALUES (@coordinatorInstanceId, @chatId, @mutedUntil)
              ON CONFLICT (CoordinatorInstanceId, ChatId) DO UPDATE SET MutedUntil = excluded.MutedUntil
              """
            : "DELETE FROM MutedChats WHERE CoordinatorInstanceId = @coordinatorInstanceId AND ChatId = @chatId",
            new { coordinatorInstanceId, chatId, mutedUntil });
    }

    private long Now() => _time.GetUtcNow().ToUnixTimeMilliseconds();
}

internal sealed record MutedChatRow(int ChatId, long? MutedUntil);
