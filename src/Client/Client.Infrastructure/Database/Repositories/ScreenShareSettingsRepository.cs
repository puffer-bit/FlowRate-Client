using Client.Application.Ports.Repositories;
using Client.Core.Media;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class ScreenShareSettingsRepository : IScreenShareSettingsRepository
{
    private readonly SqliteDatabase _database;

    public ScreenShareSettingsRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<ScreenShareSettings> GetAsync()
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<ScreenShareSettingsRow>(
            "SELECT MaxHeight, Framerate, BitrateKbps, IsAudioEnabled, AudioAppKey FROM ScreenShareSettings WHERE Id = 1");

        if (row == null)
            return new ScreenShareSettings();

        // A value an earlier build offered may not be among the choices any more
        return new ScreenShareSettings
        {
            MaxHeight = row.MaxHeight, Framerate = row.Framerate, BitrateKbps = row.BitrateKbps,
            IsAudioEnabled = row.IsAudioEnabled, AudioAppKey = row.AudioAppKey
        }.Normalize();
    }

    public async Task SaveAsync(ScreenShareSettings settings)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO ScreenShareSettings (Id, MaxHeight, Framerate, BitrateKbps, IsAudioEnabled, AudioAppKey)
            VALUES (1, @MaxHeight, @Framerate, @BitrateKbps, @IsAudioEnabled, @AudioAppKey)
            ON CONFLICT (Id) DO UPDATE SET
                MaxHeight = excluded.MaxHeight,
                Framerate = excluded.Framerate,
                BitrateKbps = excluded.BitrateKbps,
                IsAudioEnabled = excluded.IsAudioEnabled,
                AudioAppKey = excluded.AudioAppKey
            """, new { settings.MaxHeight, settings.Framerate, settings.BitrateKbps, settings.IsAudioEnabled, settings.AudioAppKey });
    }
}

internal sealed record ScreenShareSettingsRow(int MaxHeight, int Framerate, int? BitrateKbps, bool IsAudioEnabled,
    string? AudioAppKey);
