using Client.Application.Settings.Search;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using Avalonia.Media;
using Client.Avalonia.Localization;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Services;
using ReactiveUI;
using Client.Core.Settings.Themes;
using Client.Application.Settings;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

public class AppearanceControlVM : ReactiveObject, ISettingVM
{
    public string Name => SettingText.TabName("appearance");
    public SettingTabType Type => SettingTabType.AppAppearance;
    public bool IsEnabled => true;

    public IReadOnlyList<SettingSearchEntry> SearchEntries => SettingText.Entries("appearance",
        ("language", ["language"]),
        ("theme", ["theme"]),
        ("scale", ["scale"]),
        ("buttonColors", ["primaryColor", "secondaryColor", "dangerColor", "saveColor", "successColor", "warningColor",
            "errorColor", "resetColors"]),
        ("animations", ["animations", "animationSpeed"]));

    // Custom themes are not supported yet
    public ThemeType SelectedThemeType
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The operating system first, then every language the application has a translation for.
    /// </summary>
    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(null, Strings.Observe("settings.appearance.language.system")),
        ..Strings.Languages.Select(language => new LanguageOption(language.Code, Observable.Return(language.Name),
            language.IsMachineTranslated))
    ];

    public LanguageOption SelectedLanguage
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // The pickers show the color that is in effect: the chosen one or, without a choice, the one of the theme
    public Color PrimaryColor
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public Color SecondaryColor
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public Color DangerColor
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public Color SaveColor
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public Color SuccessColor
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public Color WarningColor
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public Color ErrorColor
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool HasCustomColors
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsAnimationEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    // Index into the speed choices of the view, in the order of AnimationSpeed
    public int AnimationSpeedIndex
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = (int)AnimationSpeed.Normal;

    public IReadOnlyList<int> InterfaceScales => InterfaceScale.Choices;

    public int SelectedInterfaceScale
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = InterfaceScale.Default;

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; init; }
    public ReactiveCommand<RxVoid, RxVoid> ResetColorsCommand { get; init; }
    
    private readonly IAppearanceService _appearanceService;
    private readonly IThemeService _themeService;
    private readonly ILanguageService _languageService;
    
    private bool _isLoaded;

    private readonly Subject<CustomColors> _customColorsToSave = new();
    private CustomColors _customColors = new();
    // Set while the pickers are filled by the view model, which is not a choice of the user
    private bool _isShowingColors;
    
    public AppearanceControlVM(IAppearanceService appearanceService, IThemeService themeService,
        ILanguageService languageService)
    {
        _appearanceService = appearanceService;
        _themeService = themeService;
        _languageService = languageService;
        
        LoadCommand = ReactiveCommand
            .CreateFromTask(LoadAsync);

        // Apply and persist the preset only on user changes, not on initial load
        this.WatchProperty(nameof(SelectedThemeType), x => x.SelectedThemeType)
            .Where(_ => _isLoaded)
            .Select(themeType => Observable.FromAsync(() => ChangeThemeAsync(themeType)))
            .Concat()
            .Subscribe();

        ResetColorsCommand = ReactiveCommand
            .CreateFromTask(ResetColorsAsync, this.WatchProperty(nameof(HasCustomColors), x => x.HasCustomColors));

        ObserveColor(this.WatchProperty(nameof(PrimaryColor), x => x.PrimaryColor),
            (colors, color) => colors with { Primary = color }, colors => colors.Primary);
        ObserveColor(this.WatchProperty(nameof(SecondaryColor), x => x.SecondaryColor),
            (colors, color) => colors with { Secondary = color }, colors => colors.Secondary);
        ObserveColor(this.WatchProperty(nameof(DangerColor), x => x.DangerColor),
            (colors, color) => colors with { Danger = color }, colors => colors.Danger);
        ObserveColor(this.WatchProperty(nameof(SaveColor), x => x.SaveColor),
            (colors, color) => colors with { Save = color }, colors => colors.Save);
        ObserveColor(this.WatchProperty(nameof(SuccessColor), x => x.SuccessColor),
            (colors, color) => colors with { Success = color }, colors => colors.Success);
        ObserveColor(this.WatchProperty(nameof(WarningColor), x => x.WarningColor),
            (colors, color) => colors with { Warning = color }, colors => colors.Warning);
        ObserveColor(this.WatchProperty(nameof(ErrorColor), x => x.ErrorColor),
            (colors, color) => colors with { Error = color }, colors => colors.Error);

        // A picker reports every step of a drag: the colors are applied at once, saved when it stops
        _customColorsToSave
            .Throttle(TimeSpan.FromMilliseconds(300))
            .Select(colors => Observable.FromAsync(() => _appearanceService.UpdateCustomColorsAsync(colors)))
            .Concat()
            .Subscribe();

        // A combo box reports no selection while its items are rebuilt
        this.WatchProperties(nameof(IsAnimationEnabled), nameof(AnimationSpeedIndex))
            .Select(vm => (IsEnabled: vm.IsAnimationEnabled, SpeedIndex: vm.AnimationSpeedIndex))
            .Where(values => _isLoaded && Enum.IsDefined((AnimationSpeed)values.SpeedIndex))
            .Select(values => new AnimationSettings { IsEnabled = values.IsEnabled, Speed = (AnimationSpeed)values.SpeedIndex })
            .Select(settings => Observable.FromAsync(() => ChangeAnimationsAsync(settings)))
            .Concat()
            .Subscribe();

        // A combo box reports no selection while its items are rebuilt
        this.WatchProperty(nameof(SelectedInterfaceScale), x => x.SelectedInterfaceScale)
            .Where(percent => _isLoaded && InterfaceScales.Contains(percent))
            .Select(percent => Observable.FromAsync(() => ChangeInterfaceScaleAsync(percent)))
            .Concat()
            .Subscribe();

        SelectedLanguage = Languages[0];

        this.WatchProperty(nameof(SelectedLanguage), x => x.SelectedLanguage)
            .Where(language => _isLoaded && language != null)
            .Select(language => Observable.FromAsync(() => ChangeLanguageAsync(language.Code)))
            .Concat()
            .Subscribe();
    }

    private async Task LoadAsync()
    {
        SelectedThemeType = await _appearanceService.GetCurrentThemeTypeAsync();
        var language = await _appearanceService.GetLanguageAsync();
        // A saved language the application has no translation for any more follows the system
        SelectedLanguage = Languages.FirstOrDefault(option => option.Code == language) ?? Languages[0];
        _customColors = await _appearanceService.GetCustomColorsAsync();
        ShowColors();
        var animations = await _appearanceService.GetAnimationSettingsAsync();
        IsAnimationEnabled = animations.IsEnabled;
        AnimationSpeedIndex = (int)animations.Speed;
        SelectedInterfaceScale = await _appearanceService.GetInterfaceScaleAsync();
        _isLoaded = true;
    }

    private async Task ChangeThemeAsync(ThemeType themeType)
    {
        _themeService.ApplyTheme(themeType);
        // Colors that are not chosen differ between the themes
        ShowColors();
        await _appearanceService.UpdateCurrentThemeTypeAsync(themeType);
    }

    private void ObserveColor(IObservable<Color> picked, Func<CustomColors, string?, CustomColors> set,
        Func<ThemeColors, Color> get)
    {
        picked
            .Where(_ => _isLoaded && !_isShowingColors)
            .Subscribe(color =>
            {
                // Picking the color the theme has anyway takes the choice back
                var themeColor = get(_themeService.ResolveColors(set(_customColors, null), SelectedThemeType));
                var opaque = Color.FromRgb(color.R, color.G, color.B);
                _customColors = set(_customColors,
                    opaque == themeColor ? null : $"#{opaque.R:X2}{opaque.G:X2}{opaque.B:X2}");

                _themeService.ApplyCustomColors(_customColors);
                HasCustomColors = _customColors != new CustomColors();
                _customColorsToSave.OnNext(_customColors);
            });
    }

    private void ShowColors()
    {
        var colors = _themeService.ResolveColors(_customColors, SelectedThemeType);

        _isShowingColors = true;
        PrimaryColor = colors.Primary;
        SecondaryColor = colors.Secondary;
        DangerColor = colors.Danger;
        SaveColor = colors.Save;
        SuccessColor = colors.Success;
        WarningColor = colors.Warning;
        ErrorColor = colors.Error;
        _isShowingColors = false;

        HasCustomColors = _customColors != new CustomColors();
    }

    private async Task ResetColorsAsync()
    {
        _customColors = new CustomColors();
        _themeService.ApplyCustomColors(_customColors, isAnimated: true);
        ShowColors();
        // Queued as well: a save that is still waiting must not bring the colors back after this one
        _customColorsToSave.OnNext(_customColors);
        await _appearanceService.UpdateCustomColorsAsync(_customColors);
    }

    private async Task ChangeAnimationsAsync(AnimationSettings settings)
    {
        _themeService.ApplyAnimations(settings);
        await _appearanceService.UpdateAnimationSettingsAsync(settings);
    }

    private async Task ChangeInterfaceScaleAsync(int percent)
    {
        _themeService.ApplyInterfaceScale(percent);
        await _appearanceService.UpdateInterfaceScaleAsync(percent);
    }

    private async Task ChangeLanguageAsync(string? language)
    {
        _languageService.Apply(language);
        await _appearanceService.UpdateLanguageAsync(language);
    }

    public async Task ResetAllAsync()
    {
        const ThemeType defaultTheme = ThemeType.Dark;
        
        // Applied here directly, so the change handler must not apply and save it a second time
        _isLoaded = false;
        SelectedThemeType = defaultTheme;
        SelectedLanguage = Languages[0];
        var animations = new AnimationSettings();
        IsAnimationEnabled = animations.IsEnabled;
        AnimationSpeedIndex = (int)animations.Speed;
        SelectedInterfaceScale = InterfaceScale.Default;
        _isLoaded = true;
        
        await ChangeThemeAsync(defaultTheme);
        await ChangeLanguageAsync(null);
        await ResetColorsAsync();
        await ChangeAnimationsAsync(animations);
        await ChangeInterfaceScaleAsync(InterfaceScale.Default);
    }
}
