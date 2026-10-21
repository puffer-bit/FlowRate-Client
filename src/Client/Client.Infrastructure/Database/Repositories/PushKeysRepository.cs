using Client.Application.Ports.Repositories;
using Client.Core.Push;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class PushKeysRepository : IPushKeysRepository
{
    private const string SelectKeys = "SELECT KeyId, CoordinatorInstanceId, Key FROM PushKeys";

    private readonly SqliteDatabase _database;

    public PushKeysRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<PushKey?> GetByCoordinatorAsync(string coordinatorInstanceId)
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<PushKeyRow>(
            SelectKeys + " WHERE CoordinatorInstanceId = @coordinatorInstanceId", new { coordinatorInstanceId });

        return row?.ToModel();
    }

    public async Task<PushKey?> GetByKeyIdAsync(string keyId)
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<PushKeyRow>(
            SelectKeys + " WHERE KeyId = @keyId", new { keyId = keyId.ToLowerInvariant() });

        return row?.ToModel();
    }

    public async Task SaveAsync(PushKey key)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO PushKeys (CoordinatorInstanceId, KeyId, Key)
            VALUES (@CoordinatorInstanceId, @KeyId, @Key)
            ON CONFLICT (CoordinatorInstanceId) DO UPDATE SET
                KeyId = excluded.KeyId,
                Key = excluded.Key
            """, new
            {
                key.CoordinatorInstanceId,
                KeyId = key.KeyId.ToLowerInvariant(),
                Key = Convert.ToBase64String(key.Key)
            });
    }

    public async Task SetReplyTargetAsync(string coordinatorInstanceId, PushReplyTarget target)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            UPDATE PushKeys SET Address = @Address, Port = @Port, ReplyToken = @ReplyToken
            WHERE CoordinatorInstanceId = @CoordinatorInstanceId
            """, new { CoordinatorInstanceId = coordinatorInstanceId, target.Address, target.Port, target.ReplyToken });
    }

    public async Task<PushReplyTarget?> GetReplyTargetAsync(string coordinatorInstanceId)
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<PushReplyTargetRow>(
            "SELECT Address, Port, ReplyToken FROM PushKeys WHERE CoordinatorInstanceId = @coordinatorInstanceId",
            new { coordinatorInstanceId });

        return row is { Address: { } address, Port: { } port, ReplyToken: { } replyToken }
            ? new PushReplyTarget(address, (int)port, replyToken)
            : null;
    }

    public async Task RemoveAsync(string coordinatorInstanceId)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("DELETE FROM PushKeys WHERE CoordinatorInstanceId = @coordinatorInstanceId",
            new { coordinatorInstanceId });
    }
}

internal sealed record PushKeyRow(string KeyId, string CoordinatorInstanceId, string Key)
{
    public PushKey ToModel() => new(KeyId, CoordinatorInstanceId, Convert.FromBase64String(Key));
}

internal sealed record PushReplyTargetRow(string? Address, long? Port, string? ReplyToken);
