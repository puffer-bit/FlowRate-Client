using Client.Application.Ports.Repositories;
using Client.Core.Settings.Themes;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class AppearanceSettingsRepository : IAppearanceSettingsRepository
{
    private readonly SqliteDatabase _database;

    public AppearanceSettingsRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<ThemeType> GetCurrentThemeTypeAsync()
    {
        await using var connection = await _database.OpenAsync();

        var themeType = await connection.QueryFirstOrDefaultAsync<int?>(
            "SELECT ThemeType FROM AppearanceSettings WHERE Id = 1");

        return themeType is { } value ? (ThemeType)value : ThemeType.Dark;
    }

    public async Task UpdateCurrentThemeTypeAsync(ThemeType themeType)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO AppearanceSettings (Id, ThemeType) VALUES (1, @themeType)
            ON CONFLICT (Id) DO UPDATE SET ThemeType = excluded.ThemeType
            """, new { themeType = (int)themeType });
    }

    public async Task<string?> GetLanguageAsync()
    {
        await using var connection = await _database.OpenAsync();

        return await connection.QueryFirstOrDefaultAsync<string?>(
            "SELECT LanguageCode FROM AppearanceSettings WHERE Id = 1");
    }

    public async Task UpdateLanguageAsync(string? language)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO AppearanceSettings (Id, LanguageCode) VALUES (1, @language)
            ON CONFLICT (Id) DO UPDATE SET LanguageCode = excluded.LanguageCode
            """, new { language });
    }

    public async Task<CustomColors> GetCustomColorsAsync()
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<CustomColorsRow>(
            """
            SELECT PrimaryColor, SecondaryColor, DangerColor, SaveColor, SuccessColor, WarningColor, ErrorColor
            FROM AppearanceSettings WHERE Id = 1
            """);

        if (row == null)
            return new CustomColors();

        return new CustomColors
        {
            Primary = row.PrimaryColor,
            Secondary = row.SecondaryColor,
            Danger = row.DangerColor,
            Save = row.SaveColor,
            Success = row.SuccessColor,
            Warning = row.WarningColor,
            Error = row.ErrorColor
        };
    }

    public async Task UpdateCustomColorsAsync(CustomColors colors)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO AppearanceSettings (Id, PrimaryColor, SecondaryColor, DangerColor, SaveColor,
                SuccessColor, WarningColor, ErrorColor)
            VALUES (1, @Primary, @Secondary, @Danger, @Save, @Success, @Warning, @Error)
            ON CONFLICT (Id) DO UPDATE SET
                PrimaryColor = excluded.PrimaryColor,
                SecondaryColor = excluded.SecondaryColor,
                DangerColor = excluded.DangerColor,
                SaveColor = excluded.SaveColor,
                SuccessColor = excluded.SuccessColor,
                WarningColor = excluded.WarningColor,
                ErrorColor = excluded.ErrorColor
            """, colors);
    }

    public async Task<AnimationSettings> GetAnimationSettingsAsync()
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<AnimationSettingsRow>(
            "SELECT IsAnimationEnabled, AnimationSpeed FROM AppearanceSettings WHERE Id = 1");

        if (row == null)
            return new AnimationSettings();

        return new AnimationSettings
        {
            IsEnabled = row.IsAnimationEnabled,
            // A speed this build does not know is taken as the normal one
            Speed = Enum.IsDefined((AnimationSpeed)row.AnimationSpeed)
                ? (AnimationSpeed)row.AnimationSpeed
                : AnimationSpeed.Normal
        };
    }

    public async Task UpdateAnimationSettingsAsync(AnimationSettings settings)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO AppearanceSettings (Id, IsAnimationEnabled, AnimationSpeed)
            VALUES (1, @IsEnabled, @Speed)
            ON CONFLICT (Id) DO UPDATE SET
                IsAnimationEnabled = excluded.IsAnimationEnabled,
                AnimationSpeed = excluded.AnimationSpeed
            """, new { settings.IsEnabled, Speed = (int)settings.Speed });
    }

    public async Task<int> GetInterfaceScaleAsync()
    {
        await using var connection = await _database.OpenAsync();

        var percent = await connection.QueryFirstOrDefaultAsync<int?>(
            "SELECT InterfaceScale FROM AppearanceSettings WHERE Id = 1");

        return percent is { } value ? InterfaceScale.Normalize(value) : InterfaceScale.Default;
    }

    public async Task UpdateInterfaceScaleAsync(int percent)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO AppearanceSettings (Id, InterfaceScale) VALUES (1, @percent)
            ON CONFLICT (Id) DO UPDATE SET InterfaceScale = excluded.InterfaceScale
            """, new { percent });
    }

    public async Task<bool> GetUseCustomTitleBarAsync()
    {
        await using var connection = await _database.OpenAsync();

        return await connection.QueryFirstOrDefaultAsync<bool?>(
            "SELECT UseCustomTitleBar FROM AppearanceSettings WHERE Id = 1") ?? false;
    }

    public async Task UpdateUseCustomTitleBarAsync(bool useCustomTitleBar)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO AppearanceSettings (Id, UseCustomTitleBar) VALUES (1, @useCustomTitleBar)
            ON CONFLICT (Id) DO UPDATE SET UseCustomTitleBar = excluded.UseCustomTitleBar
            """, new { useCustomTitleBar });
    }

    public async Task<bool> GetIsExperimentalEnabledAsync()
    {
        await using var connection = await _database.OpenAsync();

        return await connection.QueryFirstOrDefaultAsync<bool?>(
            "SELECT IsExperimentalEnabled FROM AppearanceSettings WHERE Id = 1") ?? false;
    }

    public async Task UpdateIsExperimentalEnabledAsync(bool isEnabled)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO AppearanceSettings (Id, IsExperimentalEnabled) VALUES (1, @isEnabled)
            ON CONFLICT (Id) DO UPDATE SET IsExperimentalEnabled = excluded.IsExperimentalEnabled
            """, new { isEnabled });
    }
}

internal sealed record CustomColorsRow(string? PrimaryColor, string? SecondaryColor, string? DangerColor,
    string? SaveColor, string? SuccessColor, string? WarningColor, string? ErrorColor);

internal sealed record AnimationSettingsRow(bool IsAnimationEnabled, int AnimationSpeed);
