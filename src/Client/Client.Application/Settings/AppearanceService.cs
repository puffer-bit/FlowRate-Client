using Client.Core.Settings.Themes;
using Client.Application.Ports.Repositories;

namespace Client.Application.Settings;

public class AppearanceService : IAppearanceService
{
    private readonly IAppearanceSettingsRepository _appearanceSettingsRepository;
    
    public AppearanceService(IAppearanceSettingsRepository appearanceSettingsRepository)
    {
        _appearanceSettingsRepository = appearanceSettingsRepository;
    }
    
    public async Task<ThemeType> GetCurrentThemeTypeAsync()
    {
        return await _appearanceSettingsRepository.GetCurrentThemeTypeAsync();
    }

    public async Task UpdateCurrentThemeTypeAsync(ThemeType themeType)
    {
        await _appearanceSettingsRepository.UpdateCurrentThemeTypeAsync(themeType);
    }

    public async Task<string?> GetLanguageAsync()
    {
        return await _appearanceSettingsRepository.GetLanguageAsync();
    }

    public async Task UpdateLanguageAsync(string? language)
    {
        await _appearanceSettingsRepository.UpdateLanguageAsync(language);
    }

    public async Task<CustomColors> GetCustomColorsAsync()
    {
        return await _appearanceSettingsRepository.GetCustomColorsAsync();
    }

    public async Task UpdateCustomColorsAsync(CustomColors colors)
    {
        await _appearanceSettingsRepository.UpdateCustomColorsAsync(colors);
    }

    public async Task<AnimationSettings> GetAnimationSettingsAsync()
    {
        return await _appearanceSettingsRepository.GetAnimationSettingsAsync();
    }

    public async Task UpdateAnimationSettingsAsync(AnimationSettings settings)
    {
        await _appearanceSettingsRepository.UpdateAnimationSettingsAsync(settings);
    }

    public async Task<int> GetInterfaceScaleAsync()
    {
        return await _appearanceSettingsRepository.GetInterfaceScaleAsync();
    }

    public async Task UpdateInterfaceScaleAsync(int percent)
    {
        await _appearanceSettingsRepository.UpdateInterfaceScaleAsync(InterfaceScale.Normalize(percent));
    }

    public async Task<bool> GetUseCustomTitleBarAsync()
    {
        return await _appearanceSettingsRepository.GetUseCustomTitleBarAsync();
    }

    public async Task UpdateUseCustomTitleBarAsync(bool useCustomTitleBar)
    {
        await _appearanceSettingsRepository.UpdateUseCustomTitleBarAsync(useCustomTitleBar);
    }

    public async Task<bool> GetUseLibWebRTCAsync()
    {
        return await _appearanceSettingsRepository.GetUseLibWebRTCAsync();
    }

    public async Task UpdateUseLibWebRTCAsync(bool useLibWebRTC)
    {
        await _appearanceSettingsRepository.UpdateUseLibWebRTCAsync(useLibWebRTC);
    }

    public async Task<bool> GetIsExperimentalEnabledAsync()
    {
        return await _appearanceSettingsRepository.GetIsExperimentalEnabledAsync();
    }

    public async Task UpdateIsExperimentalEnabledAsync(bool isEnabled)
    {
        await _appearanceSettingsRepository.UpdateIsExperimentalEnabledAsync(isEnabled);
    }
}