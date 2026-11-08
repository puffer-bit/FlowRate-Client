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
            SELECT IsHardwareDecodingEnabled, Decoder, Av1Decoder, Encoder, Av1Encoder, IsZeroCopyEnabled,
                   IsTimedPresentationEnabled, PresentationBufferFrames
            FROM VideoSettings WHERE Id = 1
            """);

        if (row == null)
            return new VideoSettings();

        return new VideoSettings
        {
            IsHardwareDecodingEnabled = row.IsHardwareDecodingEnabled,
            Decoder = row.Decoder,
            Av1Decoder = row.Av1Decoder,
            Encoder = row.Encoder,
            Av1Encoder = row.Av1Encoder,
            IsZeroCopyEnabled = row.IsZeroCopyEnabled,
            IsTimedPresentationEnabled = row.IsTimedPresentationEnabled,
            PresentationBufferFrames = row.PresentationBufferFrames
        }.Normalize();
    }

    public async Task SaveAsync(VideoSettings settings)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO VideoSettings (Id, IsHardwareDecodingEnabled, Decoder, Av1Decoder, Encoder, Av1Encoder, IsZeroCopyEnabled,
                                       IsTimedPresentationEnabled, PresentationBufferFrames)
            VALUES (1, @IsHardwareDecodingEnabled, @Decoder, @Av1Decoder, @Encoder, @Av1Encoder, @IsZeroCopyEnabled,
                    @IsTimedPresentationEnabled, @PresentationBufferFrames)
            ON CONFLICT (Id) DO UPDATE SET
                IsHardwareDecodingEnabled = excluded.IsHardwareDecodingEnabled,
                Decoder = excluded.Decoder,
                Av1Decoder = excluded.Av1Decoder,
                Encoder = excluded.Encoder,
                Av1Encoder = excluded.Av1Encoder,
                IsZeroCopyEnabled = excluded.IsZeroCopyEnabled,
                IsTimedPresentationEnabled = excluded.IsTimedPresentationEnabled,
                PresentationBufferFrames = excluded.PresentationBufferFrames
            """, new
            {
                settings.IsHardwareDecodingEnabled,
                settings.Decoder,
                settings.Av1Decoder,
                settings.Encoder,
                settings.Av1Encoder,
                settings.IsZeroCopyEnabled,
                settings.IsTimedPresentationEnabled,
                settings.PresentationBufferFrames
            });
    }
}

internal sealed record VideoSettingsRow(bool IsHardwareDecodingEnabled, string? Decoder, string? Av1Decoder, string? Encoder, string? Av1Encoder,
    bool IsZeroCopyEnabled, bool IsTimedPresentationEnabled, int PresentationBufferFrames);
