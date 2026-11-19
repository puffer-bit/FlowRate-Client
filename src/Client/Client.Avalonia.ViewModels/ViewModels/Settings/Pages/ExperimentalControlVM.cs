using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Application.Settings;
using Client.Application.Settings.Search;
using Client.Avalonia.Localization;
using Client.Avalonia.Styles.Animation;
using Client.Services;
using ReactiveUI;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

public class ExperimentalControlVM : ReactiveObject, ISettingVM, IDisposable
{
    public string Name => Strings.Get("settings.experimental.name");
    public SettingTabType Type => SettingTabType.Experimental;
    public IReadOnlyList<SettingAnchor> Anchors =>
    [
        new("experimental.general", "settings.experimental.section.general")
    ];

    public bool IsEnabled => true;

    public IReadOnlyList<SettingSearchEntry> SearchEntries => SettingText.Entries("experimental",
        ("general", ["enable"]));

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

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; init; }

    private readonly IAppearanceService _appearanceService;

    private bool _isLoaded;

    public ExperimentalControlVM(IAppearanceService appearanceService)
    {
        _appearanceService = appearanceService;

        LoadCommand = ReactiveCommand.CreateFromTask(LoadAsync);

        // Apply and persist only on user changes, not on initial load
        this.WatchProperty(nameof(IsExperimentalEnabled), vm => vm.IsExperimentalEnabled)
            .Where(_ => _isLoaded)
            .Select(values => Observable.FromAsync(() => ChangeAsync(values)))
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
        _isLoaded = true;
    }

    private async Task ChangeAsync(bool isEnabled)
    {
        ExperimentalFeatures.Apply(isEnabled, ExperimentalFeatures.UseCustomTitleBar);
        await _appearanceService.UpdateIsExperimentalEnabledAsync(isEnabled);
    }

    public async Task ResetAllAsync()
    {
        // Applied here directly, so the change handler must not apply and save it a second time
        _isLoaded = false;
        IsExperimentalEnabled = false;
        _isLoaded = true;

        await ChangeAsync(false);
    }
}
