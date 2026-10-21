using Client.Application.Ports.Repositories;
using Client.Core.Settings;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class VideoSettingsRepository : IVideoSettingsRepository
{
    private readonly SqliteDatabase _database;

    public VideoSettingsRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<VideoSettings> GetAsync()
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<VideoSettingsRow>("""
            SELECT IsHardwareDecodingEnabled, Decoder, Encoder, IsZeroCopyEnabled,
                   IsTimedPresentationEnabled, PresentationBufferFrames, ShowFrameStats
            FROM VideoSettings WHERE Id = 1
            """);

        if (row == null)
            return new VideoSettings();

        return new VideoSettings
        {
            IsHardwareDecodingEnabled = row.IsHardwareDecodingEnabled,
            Decoder = row.Decoder,
            Encoder = row.Encoder,
            IsZeroCopyEnabled = row.IsZeroCopyEnabled,
            IsTimedPresentationEnabled = row.IsTimedPresentationEnabled,
            PresentationBufferFrames = row.PresentationBufferFrames,
            ShowFrameStats = row.ShowFrameStats
        }.Normalize();
    }

    public async Task SaveAsync(VideoSettings settings)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO VideoSettings (Id, IsHardwareDecodingEnabled, Decoder, Encoder, IsZeroCopyEnabled,
                                       IsTimedPresentationEnabled, PresentationBufferFrames, ShowFrameStats)
            VALUES (1, @IsHardwareDecodingEnabled, @Decoder, @Encoder, @IsZeroCopyEnabled,
                    @IsTimedPresentationEnabled, @PresentationBufferFrames, @ShowFrameStats)
            ON CONFLICT (Id) DO UPDATE SET
                IsHardwareDecodingEnabled = excluded.IsHardwareDecodingEnabled,
                Decoder = excluded.Decoder,
                Encoder = excluded.Encoder,
                IsZeroCopyEnabled = excluded.IsZeroCopyEnabled,
                IsTimedPresentationEnabled = excluded.IsTimedPresentationEnabled,
                PresentationBufferFrames = excluded.PresentationBufferFrames,
                ShowFrameStats = excluded.ShowFrameStats
            """, new
            {
                settings.IsHardwareDecodingEnabled,
                settings.Decoder,
                settings.Encoder,
                settings.IsZeroCopyEnabled,
                settings.IsTimedPresentationEnabled,
                settings.PresentationBufferFrames,
                settings.ShowFrameStats
            });
    }
}

internal sealed record VideoSettingsRow(bool IsHardwareDecodingEnabled, string? Decoder, string? Encoder,
    bool IsZeroCopyEnabled, bool IsTimedPresentationEnabled, int PresentationBufferFrames, bool ShowFrameStats);
