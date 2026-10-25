using Client.Application.Ports.Repositories;
using Client.Core.Coordinator;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class CoordinatorStateRepository : ICoordinatorStateRepository
{
    private readonly SqliteDatabase _database;

    public CoordinatorStateRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<bool> IsMicrophoneMutedAsync(string coordinatorInstanceId)
    {
        await using var connection = await _database.OpenAsync();

        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM CoordinatorStates WHERE CoordinatorInstanceId = @coordinatorInstanceId AND IsMicrophoneMuted = 1",
            new { coordinatorInstanceId }) > 0;
    }

    public async Task SetMicrophoneMutedAsync(string coordinatorInstanceId, bool isMuted)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO CoordinatorStates (CoordinatorInstanceId, IsMicrophoneMuted)
            VALUES (@coordinatorInstanceId, @isMuted)
            ON CONFLICT (CoordinatorInstanceId) DO UPDATE SET IsMicrophoneMuted = excluded.IsMicrophoneMuted
            """, new { coordinatorInstanceId, isMuted });
    }

    public async Task<bool> IsSoundMutedAsync(string coordinatorInstanceId)
    {
        await using var connection = await _database.OpenAsync();

        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM CoordinatorStates WHERE CoordinatorInstanceId = @coordinatorInstanceId AND IsSoundMuted = 1",
            new { coordinatorInstanceId }) > 0;
    }

    public async Task SetSoundMutedAsync(string coordinatorInstanceId, bool isMuted)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO CoordinatorStates (CoordinatorInstanceId, IsSoundMuted)
            VALUES (@coordinatorInstanceId, @isMuted)
            ON CONFLICT (CoordinatorInstanceId) DO UPDATE SET IsSoundMuted = excluded.IsSoundMuted
            """, new { coordinatorInstanceId, isMuted });
    }

    public async Task<(bool IsAfk, string? Reason)> GetAfkAsync(string coordinatorInstanceId)
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<AfkRow>(
            "SELECT IsAfk, AfkReason FROM CoordinatorStates WHERE CoordinatorInstanceId = @coordinatorInstanceId",
            new { coordinatorInstanceId });

        return row is { IsAfk: true } ? (true, row.AfkReason) : (false, null);
    }

    public async Task SetAfkAsync(string coordinatorInstanceId, bool isAfk, string? reason)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO CoordinatorStates (CoordinatorInstanceId, IsAfk, AfkReason)
            VALUES (@coordinatorInstanceId, @isAfk, @reason)
            ON CONFLICT (CoordinatorInstanceId) DO UPDATE SET IsAfk = excluded.IsAfk, AfkReason = excluded.AfkReason
            """, new { coordinatorInstanceId, isAfk, reason = isAfk ? reason : null });
    }

    public async Task<LastSession?> GetLastSessionAsync(string coordinatorInstanceId)
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<LastSessionRow>("""
            SELECT ShownChatId, IsHybridSessionShown, HybridSessionId
            FROM CoordinatorStates WHERE CoordinatorInstanceId = @coordinatorInstanceId
            """, new { coordinatorInstanceId });

        if (row == null)
            return null;

        var session = new LastSession(row.ShownChatId, row.IsHybridSessionShown, row.HybridSessionId);
        return session.IsEmpty ? null : session;
    }

    public async Task SetLastSessionAsync(string coordinatorInstanceId, LastSession? session)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO CoordinatorStates (CoordinatorInstanceId, ShownChatId, IsHybridSessionShown, HybridSessionId)
            VALUES (@coordinatorInstanceId, @ShownChatId, @IsHybridSessionShown, @HybridSessionId)
            ON CONFLICT (CoordinatorInstanceId) DO UPDATE SET
                ShownChatId = excluded.ShownChatId,
                IsHybridSessionShown = excluded.IsHybridSessionShown,
                HybridSessionId = excluded.HybridSessionId
            """, new
            {
                coordinatorInstanceId,
                session?.ShownChatId,
                IsHybridSessionShown = session?.IsHybridSessionShown ?? false,
                session?.HybridSessionId
            });
    }
}

internal sealed record LastSessionRow(int? ShownChatId, bool IsHybridSessionShown, int? HybridSessionId);

internal sealed record AfkRow(bool IsAfk, string? AfkReason);
