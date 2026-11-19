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
            SELECT JournalLevel, WriteToFiles, GStreamerLogLevel, LibWebRTCLogLevel, MediaPerfOverlay,
                   IsMediaPerfExpanded, MediaPerfSections, MediaPerfIntervalMs, ShowMediaPerfGraphs, MediaPerfJournal,
                   MediaPerfSummarySeconds, LogMediaPerfAnomalies
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
            MediaPerfOverlay = (MediaPerfOverlayMode)row.MediaPerfOverlay,
            IsMediaPerfExpanded = row.IsMediaPerfExpanded,
            MediaPerfSections = (MediaPerfSections)row.MediaPerfSections,
            MediaPerfIntervalMs = row.MediaPerfIntervalMs,
            ShowMediaPerfGraphs = row.ShowMediaPerfGraphs,
            MediaPerfJournal = (MediaPerfJournalMode)row.MediaPerfJournal,
            MediaPerfSummarySeconds = row.MediaPerfSummarySeconds,
            LogMediaPerfAnomalies = row.LogMediaPerfAnomalies
        }.Normalize();
    }

    public async Task SaveAsync(DebugSettings settings)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO DebugSettings (Id, JournalLevel, WriteToFiles, GStreamerLogLevel, LibWebRTCLogLevel,
                                       MediaPerfOverlay, IsMediaPerfExpanded, MediaPerfSections, MediaPerfIntervalMs,
                                       ShowMediaPerfGraphs, MediaPerfJournal, MediaPerfSummarySeconds,
                                       LogMediaPerfAnomalies)
            VALUES (1, @JournalLevel, @WriteToFiles, @GStreamerLogLevel, @LibWebRTCLogLevel, @MediaPerfOverlay,
                    @IsMediaPerfExpanded, @MediaPerfSections, @MediaPerfIntervalMs, @ShowMediaPerfGraphs,
                    @MediaPerfJournal, @MediaPerfSummarySeconds, @LogMediaPerfAnomalies)
            ON CONFLICT (Id) DO UPDATE SET
                JournalLevel = excluded.JournalLevel,
                WriteToFiles = excluded.WriteToFiles,
                GStreamerLogLevel = excluded.GStreamerLogLevel,
                LibWebRTCLogLevel = excluded.LibWebRTCLogLevel,
                MediaPerfOverlay = excluded.MediaPerfOverlay,
                IsMediaPerfExpanded = excluded.IsMediaPerfExpanded,
                MediaPerfSections = excluded.MediaPerfSections,
                MediaPerfIntervalMs = excluded.MediaPerfIntervalMs,
                ShowMediaPerfGraphs = excluded.ShowMediaPerfGraphs,
                MediaPerfJournal = excluded.MediaPerfJournal,
                MediaPerfSummarySeconds = excluded.MediaPerfSummarySeconds,
                LogMediaPerfAnomalies = excluded.LogMediaPerfAnomalies
            """, new
            {
                JournalLevel = (int?)settings.JournalLevel,
                settings.WriteToFiles,
                GStreamerLogLevel = (int)settings.GStreamerLogLevel,
                LibWebRTCLogLevel = (int)settings.LibWebRTCLogLevel,
                MediaPerfOverlay = (int)settings.MediaPerfOverlay,
                settings.IsMediaPerfExpanded,
                MediaPerfSections = (int)settings.MediaPerfSections,
                settings.MediaPerfIntervalMs,
                settings.ShowMediaPerfGraphs,
                MediaPerfJournal = (int)settings.MediaPerfJournal,
                settings.MediaPerfSummarySeconds,
                settings.LogMediaPerfAnomalies
            });
    }
}

internal sealed record DebugSettingsRow(int? JournalLevel, bool WriteToFiles, int GStreamerLogLevel,
    int LibWebRTCLogLevel, int MediaPerfOverlay, bool IsMediaPerfExpanded, int MediaPerfSections,
    int MediaPerfIntervalMs, bool ShowMediaPerfGraphs, int MediaPerfJournal, int MediaPerfSummarySeconds,
    bool LogMediaPerfAnomalies);
