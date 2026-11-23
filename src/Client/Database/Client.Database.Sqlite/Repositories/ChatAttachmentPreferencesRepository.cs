using Client.Application.Ports.Repositories;
using Client.Core.Chat;
using Dapper;

namespace Client.Database.Sqlite.Repositories;

internal sealed class ChatAttachmentPreferencesRepository(SqliteDatabase database) : IChatAttachmentPreferencesRepository
{
    public async Task<ChatAttachmentPreferences> GetAsync()
    {
        await using var connection = await database.OpenAsync();
        var row = await connection.QueryFirstOrDefaultAsync<Row>(
            "SELECT CompressImages, StripMetadata, CompressVideos, VideoSide FROM ChatAttachmentPreferences WHERE Id = 1");
        return row == null
            ? new ChatAttachmentPreferences()
            : new ChatAttachmentPreferences(row.CompressImages, row.StripMetadata, row.CompressVideos, row.VideoSide);
    }

    public async Task SaveAsync(ChatAttachmentPreferences preferences)
    {
        await using var connection = await database.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO ChatAttachmentPreferences (Id, CompressImages, StripMetadata, CompressVideos, VideoSide)
            VALUES (1, @CompressImages, @StripMetadata, @CompressVideos, @VideoSide)
            ON CONFLICT (Id) DO UPDATE SET CompressImages = excluded.CompressImages, StripMetadata = excluded.StripMetadata,
                CompressVideos = excluded.CompressVideos, VideoSide = excluded.VideoSide
            """, new { preferences.CompressImages, preferences.StripMetadata, preferences.CompressVideos, preferences.VideoSide });
    }

    // Dapper.AOT generates the row factory outside this class and must be able to access the type.
    internal sealed class Row
    {
        public bool CompressImages { get; init; }
        public bool StripMetadata { get; init; }
        public bool CompressVideos { get; init; }
        public int? VideoSide { get; init; }
    }
}
