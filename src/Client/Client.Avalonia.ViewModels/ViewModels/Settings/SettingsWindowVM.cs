using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Client.Avalonia.Localization;
using Client.Services;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Application.Settings;
using Client.Application.Sounds;
using Client.Application.Settings.Search;
using Client.ViewModels.Dialogs;
using Result = Client.ViewModels.Dialogs.Result;
using Client.ViewModels.Settings.Pages;
using Client.Tools.AppInfo;

namespace Client.ViewModels.Settings;

public class SettingsWindowVM : ReactiveObject, IDisposable
{
    public bool IsLoaded
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ObservableCollection<ISettingVM> SettingList { get; } = new ObservableCollection<ISettingVM>();

    /// <summary>
    /// The keys of the sections that are collapsed on the tabs. The view changes the collection as
    /// the sections are collapsed and shown, and every change is saved.
    /// </summary>
    public ObservableCollection<string> CollapsedSections { get; } = new ObservableCollection<string>();
    
    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; init; }
    public ReactiveCommand<RxVoid, RxVoid> ResetAllSettingsCommand { get; init; }
    
    public Interaction<RxVoid, RxVoid> CloseWindow { get; init; }
    public Interaction<MessageBoxVM, Result> ShowMessageBox { get; init; }

    public ISettingVM? SelectedSetting
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// What is typed into the search field. While it is not blank, the list of tabs gives way to
    /// <see cref="SearchResults"/>.
    /// </summary>
    public string? SearchQuery
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            UpdateSearchResults();
        }
    }

    public IReadOnlyList<SettingSearchResultVM> SearchResults
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = [];

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchQuery);
    public bool HasNoSearchResults => IsSearching && SearchResults.Count == 0;

    /// <summary>
    /// The result picked in the list: picking one opens its tab and shows the setting.
    /// </summary>
    public SettingSearchResultVM? SelectedSearchResult
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            if (value != null)
                _ = ShowSearchResult(value);
        }
    }

    /// <summary>
    /// Asks the view to scroll to the setting with the given key on the tab that has just been opened.
    /// </summary>
    public Interaction<string, RxVoid> ShowSetting { get; } = new();

    private readonly ICollapsedSectionsRepository _collapsedSectionsRepository;

    // The last save of a collapsed section. The next one waits for it, so they land in the order they were made
    private Task _collapsedSectionSaving = Task.CompletedTask;

    // Built on the first search and again after the language changed
    private IReadOnlyList<SettingSearchResultVM>? _searchIndex;

    public bool IsReset
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public SettingsWindowVM(
        IAccountingService accountingService,
        IDatabaseSecurityService databaseSecurity,
        IAppearanceService appearanceService,
        IBehaviorService behaviorService,
        IStorageService storageService,
        IVideoService videoService,
        IMediaCapabilities mediaCapabilities,
        IAudioService audioService,
        IMicrophoneTester microphoneTester,
        ISoundService soundService,
        ISoundPackCatalog soundPackCatalog,
        IDebugAndLogService debugAndLogService,
        IPlatformSpecificService platformSpecificService,
        IHotKeysService hotKeysService,
        IThemeService themeService,
        ILanguageService languageService,
        ICollapsedSectionsRepository collapsedSectionsRepository)
    {
        _collapsedSectionsRepository = collapsedSectionsRepository;
        CloseWindow = new Interaction<RxVoid, RxVoid>();
        ShowMessageBox = new Interaction<MessageBoxVM, Result>();
        
        CloseCommand = ReactiveCommand
            .CreateFromTask(CloseAsync);
        ResetAllSettingsCommand = ReactiveCommand
            .CreateFromTask(ResetAllSettings);
        
        SettingList.Add(new AccountingControlVM(accountingService, databaseSecurity));
        SettingList.Add(new BehaviorControlVM(behaviorService));
        SettingList.Add(new AudioControlVM(audioService, microphoneTester, soundService, soundPackCatalog));
        SettingList.Add(new VideoControlVM(videoService, mediaCapabilities));
        SettingList.Add(new AppearanceControlVM(appearanceService, themeService, languageService));
        SettingList.Add(new StorageControlVM(storageService));
        SettingList.Add(new HotKeysControlVM(hotKeysService));
        SettingList.Add(new PlatformSpecificControlVM(platformSpecificService));
        SettingList.Add(new DebugAndLogsControlVM(debugAndLogService));
        SettingList.Add(new ExperimentalControlVM(appearanceService));
        SettingList.Add(new AboutControlVM(AppInformation.GetClientBuildVersion()));

        Strings.LanguageChanged += OnLanguageChanged;

        _ = LoadAllSettings();
    }

    private void OnLanguageChanged()
    {
        foreach (var setting in SettingList)
            setting.RaisePropertyChanged(nameof(ISettingVM.Name));

        // The index holds the texts of the previous language
        _searchIndex = null;
        if (IsSearching)
            UpdateSearchResults();
    }

    private async Task LoadAllSettings()
    {
        var loadTasks = SettingList
            .Select(setting => TaskObservableExtensions.ToTask(setting.LoadCommand.Execute()));

        await Task.WhenAll(loadTasks);

        // Before the first tab is shown, so that it opens with its sections as they were left
        foreach (var key in await _collapsedSectionsRepository.GetAsync())
            CollapsedSections.Add(key);
        CollapsedSections.CollectionChanged += OnCollapsedSectionsChanged;

        SelectedSetting = SettingList.FirstOrDefault(c => c.IsEnabled);
        
        IsLoaded = true;
    }
    
    private void OnCollapsedSectionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (string key in e.OldItems ?? Array.Empty<string>())
            _collapsedSectionSaving = SaveCollapsedSection(_collapsedSectionSaving, key, false);
        foreach (string key in e.NewItems ?? Array.Empty<string>())
            _collapsedSectionSaving = SaveCollapsedSection(_collapsedSectionSaving, key, true);
    }

    private async Task SaveCollapsedSection(Task previous, string key, bool isCollapsed)
    {
        // A save that failed does not hold back the ones after it
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await _collapsedSectionsRepository.SetAsync(key, isCollapsed);
    }

    private void UpdateSearchResults()
    {
        // Tabs that cannot be opened are not searched either
        _searchIndex ??= SettingList
            .Where(tab => tab.IsEnabled)
            .SelectMany(tab => tab.SearchEntries.Select(entry => new SettingSearchResultVM(tab, entry)))
            .ToArray();

        SelectedSearchResult = null;
        SearchResults = SettingsSearch.Find(SearchQuery, _searchIndex, result => result.Entry);

        this.RaisePropertyChanged(nameof(IsSearching));
        this.RaisePropertyChanged(nameof(HasNoSearchResults));
    }

    /// <summary>
    /// Opens the first result, as Enter in the search field does.
    /// </summary>
    public void ShowFirstSearchResult()
    {
        if (SearchResults.Count > 0)
            SelectedSearchResult = SearchResults[0];
    }

    private async Task ShowSearchResult(SettingSearchResultVM result)
    {
        SelectedSetting = result.Tab;
        await ShowSetting.Handle(result.Entry.Key);
    }

    private async Task CloseAsync()
    {
        await CloseWindow.Handle(RxVoid.Default);
    }

    private async Task ResetAllSettings()
    {
        if (await ShowMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.YesNo, 
                Strings.Get("settings.reset.question"), Strings.Get("common.warning"))) == Result.Yes)
        {
            foreach (var setting in SettingList)
            {
                await setting.ResetAllAsync();
            }
            IsReset = true;
        }
    }

    public void Dispose()
    {
        Strings.LanguageChanged -= OnLanguageChanged;
        CollapsedSections.CollectionChanged -= OnCollapsedSectionsChanged;

        foreach (var setting in SettingList.OfType<IDisposable>())
        {
            setting.Dispose();
        }
    }
}