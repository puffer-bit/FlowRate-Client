using Client.Application.Ports.Repositories;
using Client.Core.Settings;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

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
                   UpdateChannel
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
            UpdateChannel = (UpdateChannel)row.UpdateChannel
        };
    }

    public async Task SaveAsync(BehaviorSettings settings)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO BehaviorSettings (Id, IsAutoConnectEnabled, IsFallbackConnectionEnabled,
                IsFallbackConnectionWarningEnabled, IsPropagatedConnectionWarningEnabled, CloseButtonAction,
                IsUpdateCheckOnStartupEnabled, UpdateChannel)
            VALUES (1, @IsAutoConnectEnabled, @IsFallbackConnectionEnabled,
                @IsFallbackConnectionWarningEnabled, @IsPropagatedConnectionWarningEnabled, @CloseButtonAction,
                @IsUpdateCheckOnStartupEnabled, @UpdateChannel)
            ON CONFLICT (Id) DO UPDATE SET
                IsAutoConnectEnabled = excluded.IsAutoConnectEnabled,
                IsFallbackConnectionEnabled = excluded.IsFallbackConnectionEnabled,
                IsFallbackConnectionWarningEnabled = excluded.IsFallbackConnectionWarningEnabled,
                IsPropagatedConnectionWarningEnabled = excluded.IsPropagatedConnectionWarningEnabled,
                CloseButtonAction = excluded.CloseButtonAction,
                IsUpdateCheckOnStartupEnabled = excluded.IsUpdateCheckOnStartupEnabled,
                UpdateChannel = excluded.UpdateChannel
            """, new
            {
                settings.IsAutoConnectEnabled,
                settings.IsFallbackConnectionEnabled,
                settings.IsFallbackConnectionWarningEnabled,
                settings.IsPropagatedConnectionWarningEnabled,
                CloseButtonAction = (int)settings.CloseButtonAction,
                settings.IsUpdateCheckOnStartupEnabled,
                UpdateChannel = (int)settings.UpdateChannel
            });
    }

    public async Task<(string CoordinatorId, int UserId)?> GetAutoConnectInstanceAsync()
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<AutoConnectRow>(
            "SELECT AutoConnectCoordinatorId, AutoConnectUserId FROM BehaviorSettings WHERE Id = 1");

        if (row is not { AutoConnectCoordinatorId: { } coordinatorId, AutoConnectUserId: { } userId })
            return null;

        return (coordinatorId, userId);
    }

    public async Task SetAutoConnectInstanceAsync((string CoordinatorId, int UserId)? identity)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO BehaviorSettings (Id, AutoConnectCoordinatorId, AutoConnectUserId)
            VALUES (1, @coordinatorId, @userId)
            ON CONFLICT (Id) DO UPDATE SET
                AutoConnectCoordinatorId = excluded.AutoConnectCoordinatorId,
                AutoConnectUserId = excluded.AutoConnectUserId
            """, new { coordinatorId = identity?.CoordinatorId, userId = identity?.UserId });
    }
}

internal sealed record BehaviorSettingsRow(bool IsAutoConnectEnabled, bool IsFallbackConnectionEnabled,
    bool IsFallbackConnectionWarningEnabled, bool IsPropagatedConnectionWarningEnabled, int CloseButtonAction,
    bool IsUpdateCheckOnStartupEnabled, int UpdateChannel);

internal sealed record AutoConnectRow(string? AutoConnectCoordinatorId, int? AutoConnectUserId);
