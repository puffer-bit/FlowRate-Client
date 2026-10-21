using Client.Application.Ports.Repositories;
using Client.Core.Settings;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class HotKeysSettingsRepository : IHotKeysSettingsRepository
{
    private readonly SqliteDatabase _database;

    public HotKeysSettingsRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<HotKeysSettings> GetAsync()
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<HotKeysSettingsRow>("""
            SELECT IsEnabled, IsGlobalEnabled FROM HotKeysSettings WHERE Id = 1
            """);
        var keys = await connection.QueryAsync<HotKeyRow>("""
            SELECT Action, Gesture, IsGlobal FROM HotKeys
            """);

        var settings = new HotKeysSettings
        {
            IsEnabled = row?.IsEnabled ?? true,
            IsGlobalEnabled = row?.IsGlobalEnabled ?? true
        };

        // Actions this version does not know are skipped
        foreach (var key in keys)
        {
            var action = (HotKeyAction)key.Action;
            if (HotKeysSettings.Actions.Contains(action))
                settings = settings.With(new HotKeyBinding(action, key.Gesture, key.IsGlobal));
        }

        return settings;
    }

    public async Task SaveAsync(HotKeysSettings settings)
    {
        await using var connection = await _database.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await connection.ExecuteAsync("""
            INSERT INTO HotKeysSettings (Id, IsEnabled, IsGlobalEnabled)
            VALUES (1, @IsEnabled, @IsGlobalEnabled)
            ON CONFLICT (Id) DO UPDATE SET
                IsEnabled = excluded.IsEnabled,
                IsGlobalEnabled = excluded.IsGlobalEnabled
            """, new { settings.IsEnabled, settings.IsGlobalEnabled }, transaction);

        foreach (var binding in settings.Bindings)
        {
            await connection.ExecuteAsync("""
                INSERT INTO HotKeys (Action, Gesture, IsGlobal)
                VALUES (@Action, @Gesture, @IsGlobal)
                ON CONFLICT (Action) DO UPDATE SET
                    Gesture = excluded.Gesture,
                    IsGlobal = excluded.IsGlobal
                """, new { Action = (int)binding.Action, binding.Gesture, binding.IsGlobal }, transaction);
        }

        await transaction.CommitAsync();
    }
}

internal sealed record HotKeysSettingsRow(bool IsEnabled, bool IsGlobalEnabled);

internal sealed record HotKeyRow(int Action, string? Gesture, bool IsGlobal);
