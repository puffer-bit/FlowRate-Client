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
        var screen = await GetProfileAsync(1);
        var camera = await GetProfileAsync(2);
        return screen with { Camera = camera == new VideoSettings() ? null : camera };
    }

    private async Task<VideoSettings> GetProfileAsync(int profile)
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<VideoSettingsRow>("""
            SELECT IsHardwareDecodingEnabled, Decoder, Av1Decoder, Vp8Decoder, Vp9Decoder, Encoder, Av1Encoder, Vp8Encoder, Vp9Encoder,
                   IsZeroCopyEnabled, IsTimedPresentationEnabled, PresentationBufferFrames, IsCodecSettingsWarningAcknowledged
            FROM VideoSettings WHERE Id = @profile
            """, new { profile });

        if (row == null)
            return new VideoSettings();

        var parameters = (await connection.QueryAsync<CodecPropertyOverride>(
            "SELECT Factory, Name, Value FROM CodecParameters WHERE Profile = @profile ORDER BY Factory, Name", new { profile })).ToArray();

        return new VideoSettings
        {
            IsHardwareDecodingEnabled = row.IsHardwareDecodingEnabled,
            Decoder = row.Decoder,
            Av1Decoder = row.Av1Decoder,
            Vp8Decoder = row.Vp8Decoder,
            Vp9Decoder = row.Vp9Decoder,
            Encoder = row.Encoder,
            Av1Encoder = row.Av1Encoder,
            Vp8Encoder = row.Vp8Encoder,
            Vp9Encoder = row.Vp9Encoder,
            CodecOverrides = parameters,
            IsCodecSettingsWarningAcknowledged = row.IsCodecSettingsWarningAcknowledged,
            IsZeroCopyEnabled = row.IsZeroCopyEnabled,
            IsTimedPresentationEnabled = row.IsTimedPresentationEnabled,
            PresentationBufferFrames = row.PresentationBufferFrames
        }.Normalize();
    }

    public async Task SaveAsync(VideoSettings settings)
    {
        await using var connection = await _database.OpenAsync();
        using var transaction = connection.BeginTransaction();

        await SaveProfileAsync(connection, transaction, settings, 1);
        await SaveProfileAsync(connection, transaction, settings.Camera ?? new VideoSettings(), 2);
        transaction.Commit();
    }

    private static async Task SaveProfileAsync(System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction, VideoSettings settings, int profile)
    {
        await connection.ExecuteAsync("""
            INSERT INTO VideoSettings (Id, IsHardwareDecodingEnabled, Decoder, Av1Decoder, Vp8Decoder, Vp9Decoder,
                                       Encoder, Av1Encoder, Vp8Encoder, Vp9Encoder, IsZeroCopyEnabled,
                                       IsTimedPresentationEnabled, PresentationBufferFrames, IsCodecSettingsWarningAcknowledged)
            VALUES (@profile, @IsHardwareDecodingEnabled, @Decoder, @Av1Decoder, @Vp8Decoder, @Vp9Decoder,
                    @Encoder, @Av1Encoder, @Vp8Encoder, @Vp9Encoder, @IsZeroCopyEnabled,
                    @IsTimedPresentationEnabled, @PresentationBufferFrames, @IsCodecSettingsWarningAcknowledged)
            ON CONFLICT (Id) DO UPDATE SET
                IsHardwareDecodingEnabled = excluded.IsHardwareDecodingEnabled,
                Decoder = excluded.Decoder,
                Av1Decoder = excluded.Av1Decoder,
                Vp8Decoder = excluded.Vp8Decoder,
                Vp9Decoder = excluded.Vp9Decoder,
                Encoder = excluded.Encoder,
                Av1Encoder = excluded.Av1Encoder,
                Vp8Encoder = excluded.Vp8Encoder,
                Vp9Encoder = excluded.Vp9Encoder,
                IsZeroCopyEnabled = excluded.IsZeroCopyEnabled,
                IsTimedPresentationEnabled = excluded.IsTimedPresentationEnabled,
                PresentationBufferFrames = excluded.PresentationBufferFrames,
                IsCodecSettingsWarningAcknowledged = excluded.IsCodecSettingsWarningAcknowledged
            """, new
            {
                profile,
                settings.IsHardwareDecodingEnabled,
                settings.Decoder,
                settings.Av1Decoder,
                settings.Vp8Decoder,
                settings.Vp9Decoder,
                settings.Encoder,
                settings.Av1Encoder,
                settings.Vp8Encoder,
                settings.Vp9Encoder,
                settings.IsZeroCopyEnabled,
                settings.IsTimedPresentationEnabled,
                settings.IsCodecSettingsWarningAcknowledged,
                settings.PresentationBufferFrames
            }, transaction);

        await connection.ExecuteAsync("DELETE FROM CodecParameters WHERE Profile = @profile", new { profile }, transaction: transaction);
        foreach (var parameter in settings.CodecOverrides)
            await connection.ExecuteAsync("INSERT INTO CodecParameters (Profile, Factory, Name, Value) VALUES (@profile, @Factory, @Name, @Value)",
                new { profile, parameter.Factory, parameter.Name, parameter.Value }, transaction);
    }
}

internal sealed record VideoSettingsRow(bool IsHardwareDecodingEnabled,
    string? Decoder, string? Av1Decoder, string? Vp8Decoder, string? Vp9Decoder,
    string? Encoder, string? Av1Encoder, string? Vp8Encoder, string? Vp9Encoder,
    bool IsZeroCopyEnabled, bool IsTimedPresentationEnabled, int PresentationBufferFrames, bool IsCodecSettingsWarningAcknowledged);
