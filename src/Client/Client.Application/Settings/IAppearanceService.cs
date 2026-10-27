using Client.Core.Settings.Themes;


namespace Client.Application.Settings;

public interface IAppearanceService
{
    Task<ThemeType> GetCurrentThemeTypeAsync();
    Task UpdateCurrentThemeTypeAsync(ThemeType themeType);

    /// <returns>Code of the interface language, or null to follow the operating system.</returns>
    Task<string?> GetLanguageAsync();
    Task UpdateLanguageAsync(string? language);

    /// <returns>Colors chosen instead of the ones of the theme.</returns>
    Task<CustomColors> GetCustomColorsAsync();
    Task UpdateCustomColorsAsync(CustomColors colors);

    Task<AnimationSettings> GetAnimationSettingsAsync();
    Task UpdateAnimationSettingsAsync(AnimationSettings settings);

    /// <returns>Size of the interface in percent, one of <see cref="InterfaceScale.Choices"/>.</returns>
    Task<int> GetInterfaceScaleAsync();
    Task UpdateInterfaceScaleAsync(int percent);

    /// <returns>Whether the main window draws its own title bar instead of the one of the system.</returns>
    Task<bool> GetUseCustomTitleBarAsync();
    Task UpdateUseCustomTitleBarAsync(bool useCustomTitleBar);

    /// <returns>Whether the experimental features are enabled as a whole.</returns>
    Task<bool> GetIsExperimentalEnabledAsync();
    Task UpdateIsExperimentalEnabledAsync(bool isEnabled);
}