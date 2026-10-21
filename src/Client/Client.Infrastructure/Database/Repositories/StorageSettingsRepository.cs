using Client.Application.Ports.Repositories;
using Client.Core.Settings;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class StorageSettingsRepository : IStorageSettingsRepository
{
    private readonly SqliteDatabase _database;

    public StorageSettingsRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<StorageSettings> GetAsync()
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<StorageSettingsRow>("""
            SELECT IsChatCacheEnabled, ChatCacheMessagesPerChat, MediaCacheLimitMegabytes
            FROM StorageSettings WHERE Id = 1
            """);

        if (row == null)
            return new StorageSettings();

        return new StorageSettings
        {
            IsChatCacheEnabled = row.IsChatCacheEnabled,
            ChatCacheMessagesPerChat = row.ChatCacheMessagesPerChat,
            MediaCacheLimitMegabytes = row.MediaCacheLimitMegabytes
        };
    }

    public async Task SaveAsync(StorageSettings settings)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO StorageSettings (Id, IsChatCacheEnabled, ChatCacheMessagesPerChat, MediaCacheLimitMegabytes)
            VALUES (1, @IsChatCacheEnabled, @ChatCacheMessagesPerChat, @MediaCacheLimitMegabytes)
            ON CONFLICT (Id) DO UPDATE SET
                IsChatCacheEnabled = excluded.IsChatCacheEnabled,
                ChatCacheMessagesPerChat = excluded.ChatCacheMessagesPerChat,
                MediaCacheLimitMegabytes = excluded.MediaCacheLimitMegabytes
            """, new
            {
                settings.IsChatCacheEnabled,
                settings.ChatCacheMessagesPerChat,
                settings.MediaCacheLimitMegabytes
            });
    }
}

internal sealed record StorageSettingsRow(bool IsChatCacheEnabled, int ChatCacheMessagesPerChat,
    int MediaCacheLimitMegabytes);
