using Client.Application.Settings;
using Client.Application.Settings.Search;
using Client.Avalonia.Localization;
using Client.Avalonia.Styles.Animation;
using Client.Services;
using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

public class ExperimentalControlVM : ReactiveObject, ISettingVM, IDisposable
{
    public string Name => Strings.Get("settings.experimental.name");
    public SettingTabType Type => SettingTabType.Experimental;
    public bool IsEnabled => true;

    public IReadOnlyList<SettingSearchEntry> SearchEntries => SettingText.Entries("experimental",
        ("general", ["enable"]),
        ("window", ["customTitleBar"]));

    /// <summary>
    /// The experimental features as a whole: while off, none of them works and their settings cannot
    /// be changed. Known before the tab is loaded, for the icon in the list of tabs.
    /// </summary>
    public bool IsExperimentalEnabled
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsIconAgitated));
        }
    } = ExperimentalFeatures.IsEnabled;

    public bool IsIconAgitated => IsExperimentalEnabled && Motion.IsEnabled;

    /// <summary>
    /// Whether the main window draws its own title bar instead of the one of the system.
    /// </summary>
    public bool UseCustomTitleBar
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; init; }

    private readonly IAppearanceService _appearanceService;

    private bool _isLoaded;

    public ExperimentalControlVM(IAppearanceService appearanceService)
    {
        _appearanceService = appearanceService;

        LoadCommand = ReactiveCommand
            .CreateFromTask(LoadAsync);

        // Apply and persist only on user changes, not on initial load
        this.WatchProperties(nameof(IsExperimentalEnabled), nameof(UseCustomTitleBar))
            .Where(_ => _isLoaded)
            .Select(vm => (vm.IsExperimentalEnabled, vm.UseCustomTitleBar))
            .Select(values => Observable.FromAsync(() => ChangeAsync(values.Item1, values.Item2)))
            .Concat()
            .Subscribe();

        // The icon stops and starts with the animations of the application, the moment they are switched
        Motion.Changed += OnMotionChanged;
    }

    private void OnMotionChanged() => this.RaisePropertyChanged(nameof(IsIconAgitated));

    // Motion is static and would keep the view model of every opened settings dialog alive
    public void Dispose() => Motion.Changed -= OnMotionChanged;

    private async Task LoadAsync()
    {
        IsExperimentalEnabled = await _appearanceService.GetIsExperimentalEnabledAsync();
        UseCustomTitleBar = await _appearanceService.GetUseCustomTitleBarAsync();
        _isLoaded = true;
    }

    private async Task ChangeAsync(bool isEnabled, bool useCustomTitleBar)
    {
        ExperimentalFeatures.Apply(isEnabled, useCustomTitleBar);
        await _appearanceService.UpdateIsExperimentalEnabledAsync(isEnabled);
        await _appearanceService.UpdateUseCustomTitleBarAsync(useCustomTitleBar);
    }

    public async Task ResetAllAsync()
    {
        // Applied here directly, so the change handler must not apply and save it a second time
        _isLoaded = false;
        IsExperimentalEnabled = false;
        UseCustomTitleBar = false;
        _isLoaded = true;

        await ChangeAsync(false, false);
    }
}
