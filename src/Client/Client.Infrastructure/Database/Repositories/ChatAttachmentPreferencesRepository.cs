using Client.Application.Ports.Repositories;
using Client.Core.Chat;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class ChatAttachmentPreferencesRepository(SqliteDatabase database) : IChatAttachmentPreferencesRepository
{
    public async Task<ChatAttachmentPreferences> GetAsync()
    {
        await using var connection = await database.OpenAsync();
        var row = await connection.QueryFirstOrDefaultAsync<Row>(
            "SELECT CompressImages, StripMetadata FROM ChatAttachmentPreferences WHERE Id = 1");
        return row == null ? new ChatAttachmentPreferences() : new ChatAttachmentPreferences(row.CompressImages, row.StripMetadata);
    }

    public async Task SaveAsync(ChatAttachmentPreferences preferences)
    {
        await using var connection = await database.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO ChatAttachmentPreferences (Id, CompressImages, StripMetadata)
            VALUES (1, @CompressImages, @StripMetadata)
            ON CONFLICT (Id) DO UPDATE SET CompressImages = excluded.CompressImages, StripMetadata = excluded.StripMetadata
            """, new { preferences.CompressImages, preferences.StripMetadata });
    }

    private sealed class Row
    {
        public bool CompressImages { get; init; }
        public bool StripMetadata { get; init; }
    }
}
