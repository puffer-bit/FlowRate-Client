using Client.Application.Ports.Repositories;
using Client.Core.Settings;
using Dapper;

namespace Client.Database.Sqlite.Repositories;

internal sealed class BehaviorSettingsRepository : IBehaviorSettingsRepository
{
    private readonly SqliteDatabase _database;

    public BehaviorSettingsRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<BehaviorSettings> GetAsync()
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<BehaviorSettingsRow>("""
            SELECT IsAutoConnectEnabled, IsFallbackConnectionEnabled, IsFallbackConnectionWarningEnabled,
                   IsPropagatedConnectionWarningEnabled, CloseButtonAction, IsUpdateCheckOnStartupEnabled,
                   UpdateChannel, QuickReaction, KeepScreenOnWhileWatching, IsAutoIdleEnabled, IdleAfterMinutes,
                   SleepAfterMinutes, KeepActiveDuringCalls
            FROM BehaviorSettings WHERE Id = 1
            """);

        if (row == null)
            return new BehaviorSettings();

        return new BehaviorSettings
        {
            IsAutoConnectEnabled = row.IsAutoConnectEnabled,
            IsFallbackConnectionEnabled = row.IsFallbackConnectionEnabled,
            IsFallbackConnectionWarningEnabled = row.IsFallbackConnectionWarningEnabled,
            IsPropagatedConnectionWarningEnabled = row.IsPropagatedConnectionWarningEnabled,
            CloseButtonAction = (CloseButtonAction)row.CloseButtonAction,
            IsUpdateCheckOnStartupEnabled = row.IsUpdateCheckOnStartupEnabled,
            UpdateChannel = (UpdateChannel)row.UpdateChannel,
            QuickReaction = row.QuickReaction,
            KeepScreenOnWhileWatching = row.KeepScreenOnWhileWatching,
            IsAutoIdleEnabled = row.IsAutoIdleEnabled,
            IdleAfterMinutes = row.IdleAfterMinutes,
            SleepAfterMinutes = row.SleepAfterMinutes,
            KeepActiveDuringCalls = row.KeepActiveDuringCalls
        };
    }

    public async Task SaveAsync(BehaviorSettings settings)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO BehaviorSettings (Id, IsAutoConnectEnabled, IsFallbackConnectionEnabled,
                IsFallbackConnectionWarningEnabled, IsPropagatedConnectionWarningEnabled, CloseButtonAction,
                IsUpdateCheckOnStartupEnabled, UpdateChannel, QuickReaction, KeepScreenOnWhileWatching,
                IsAutoIdleEnabled, IdleAfterMinutes, SleepAfterMinutes, KeepActiveDuringCalls)
            VALUES (1, @IsAutoConnectEnabled, @IsFallbackConnectionEnabled,
                @IsFallbackConnectionWarningEnabled, @IsPropagatedConnectionWarningEnabled, @CloseButtonAction,
                @IsUpdateCheckOnStartupEnabled, @UpdateChannel, @QuickReaction, @KeepScreenOnWhileWatching,
                @IsAutoIdleEnabled, @IdleAfterMinutes, @SleepAfterMinutes, @KeepActiveDuringCalls)
            ON CONFLICT (Id) DO UPDATE SET
                IsAutoConnectEnabled = excluded.IsAutoConnectEnabled,
                IsFallbackConnectionEnabled = excluded.IsFallbackConnectionEnabled,
                IsFallbackConnectionWarningEnabled = excluded.IsFallbackConnectionWarningEnabled,
                IsPropagatedConnectionWarningEnabled = excluded.IsPropagatedConnectionWarningEnabled,
                CloseButtonAction = excluded.CloseButtonAction,
                IsUpdateCheckOnStartupEnabled = excluded.IsUpdateCheckOnStartupEnabled,
                UpdateChannel = excluded.UpdateChannel,
                QuickReaction = excluded.QuickReaction,
                KeepScreenOnWhileWatching = excluded.KeepScreenOnWhileWatching,
                IsAutoIdleEnabled = excluded.IsAutoIdleEnabled,
                IdleAfterMinutes = excluded.IdleAfterMinutes,
                SleepAfterMinutes = excluded.SleepAfterMinutes,
                KeepActiveDuringCalls = excluded.KeepActiveDuringCalls
            """, new
            {
                settings.IsAutoConnectEnabled,
                settings.IsFallbackConnectionEnabled,
                settings.IsFallbackConnectionWarningEnabled,
                settings.IsPropagatedConnectionWarningEnabled,
                CloseButtonAction = (int)settings.CloseButtonAction,
                settings.IsUpdateCheckOnStartupEnabled,
                UpdateChannel = (int)settings.UpdateChannel,
                settings.QuickReaction,
                settings.KeepScreenOnWhileWatching,
                settings.IsAutoIdleEnabled,
                settings.IdleAfterMinutes,
                settings.SleepAfterMinutes,
                settings.KeepActiveDuringCalls
            });
    }

    public async Task<AutoConnectChoice> GetAutoConnectChoiceAsync()
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<AutoConnectRow>(
            "SELECT AutoConnectCoordinatorId, AutoConnectUserId FROM BehaviorSettings WHERE Id = 1");

        // An account without its server cannot be told apart from another server's: the whole choice is the last one
        return row is { AutoConnectCoordinatorId: { } coordinatorId }
            ? new AutoConnectChoice(coordinatorId, row.AutoConnectUserId)
            : AutoConnectChoice.Last;
    }

    public async Task SetAutoConnectChoiceAsync(AutoConnectChoice choice)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO BehaviorSettings (Id, AutoConnectCoordinatorId, AutoConnectUserId)
            VALUES (1, @coordinatorId, @userId)
            ON CONFLICT (Id) DO UPDATE SET
                AutoConnectCoordinatorId = excluded.AutoConnectCoordinatorId,
                AutoConnectUserId = excluded.AutoConnectUserId
            """, new { coordinatorId = choice.CoordinatorId, userId = choice.CoordinatorId == null ? null : choice.UserId });
    }

    public async Task<(string CoordinatorId, int? UserId)?> GetLastConnectionAsync()
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<LastConnectionRow>(
            "SELECT LastCoordinatorId, LastUserId FROM BehaviorSettings WHERE Id = 1");

        return row is { LastCoordinatorId: { } coordinatorId } ? (coordinatorId, row.LastUserId) : null;
    }
}

internal sealed record BehaviorSettingsRow(bool IsAutoConnectEnabled, bool IsFallbackConnectionEnabled,
    bool IsFallbackConnectionWarningEnabled, bool IsPropagatedConnectionWarningEnabled, int CloseButtonAction,
    bool IsUpdateCheckOnStartupEnabled, int UpdateChannel, string QuickReaction, bool KeepScreenOnWhileWatching,
    bool IsAutoIdleEnabled, int IdleAfterMinutes, int SleepAfterMinutes, bool KeepActiveDuringCalls);

internal sealed record AutoConnectRow(string? AutoConnectCoordinatorId, int? AutoConnectUserId);

internal sealed record LastConnectionRow(string? LastCoordinatorId, int? LastUserId);
