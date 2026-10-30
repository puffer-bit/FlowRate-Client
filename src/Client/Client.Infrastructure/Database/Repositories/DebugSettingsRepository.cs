using Client.Application.Ports.Repositories;
using Client.Core.Settings;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class DebugSettingsRepository : IDebugSettingsRepository
{
    private readonly SqliteDatabase _database;

    public DebugSettingsRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<DebugSettings> GetAsync()
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<DebugSettingsRow>("""
            SELECT JournalLevel, WriteToFiles, GStreamerLogLevel, LibWebRTCLogLevel, ShowFrameStats
            FROM DebugSettings WHERE Id = 1
            """);

        if (row == null)
            return new DebugSettings();

        return new DebugSettings
        {
            JournalLevel = (JournalLevel?)row.JournalLevel,
            WriteToFiles = row.WriteToFiles,
            GStreamerLogLevel = (NativeLogLevel)row.GStreamerLogLevel,
            LibWebRTCLogLevel = (NativeLogLevel)row.LibWebRTCLogLevel,
            ShowFrameStats = row.ShowFrameStats
        }.Normalize();
    }

    public async Task SaveAsync(DebugSettings settings)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO DebugSettings (Id, JournalLevel, WriteToFiles, GStreamerLogLevel, LibWebRTCLogLevel,
                                       ShowFrameStats)
            VALUES (1, @JournalLevel, @WriteToFiles, @GStreamerLogLevel, @LibWebRTCLogLevel, @ShowFrameStats)
            ON CONFLICT (Id) DO UPDATE SET
                JournalLevel = excluded.JournalLevel,
                WriteToFiles = excluded.WriteToFiles,
                GStreamerLogLevel = excluded.GStreamerLogLevel,
                LibWebRTCLogLevel = excluded.LibWebRTCLogLevel,
                ShowFrameStats = excluded.ShowFrameStats
            """, new
            {
                JournalLevel = (int?)settings.JournalLevel,
                settings.WriteToFiles,
                GStreamerLogLevel = (int)settings.GStreamerLogLevel,
                LibWebRTCLogLevel = (int)settings.LibWebRTCLogLevel,
                settings.ShowFrameStats
            });
    }
}

internal sealed record DebugSettingsRow(int? JournalLevel, bool WriteToFiles, int GStreamerLogLevel,
    int LibWebRTCLogLevel, bool ShowFrameStats);
