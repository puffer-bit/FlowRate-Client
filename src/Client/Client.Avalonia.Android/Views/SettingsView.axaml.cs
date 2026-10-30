using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Application.Settings;
using Client.Application.Settings.Search;
using Client.Application.Sounds;
using Client.Avalonia.Controls.Settings;
using Client.Avalonia.Localization;
using Client.Droid.Platform;
using Client.Services;
using Client.ViewModels.Dialogs;
using Client.ViewModels.Settings;
using Client.ViewModels.Settings.Pages;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;

namespace Client.Droid.Views;

/// <summary>
/// The settings on the whole screen, as the settings of a phone are: a list of categories first, and
/// a tap on one goes into its page. The pages answer the view models of the desktop tabs and show the
/// settings of them that mean something on Android.
/// </summary>
public partial class SettingsView : UserControl
{
    private readonly AppearanceControlVM? _appearance;
    private readonly BehaviorControlVM? _behavior;
    private readonly AudioControlVM? _audio;
    private readonly StorageControlVM? _storage;
    private readonly DebugAndLogsControlVM? _debug;
    private readonly AccountingControlVM? _accounting;
    private readonly AboutControlVM? _about;
    private readonly Func<MessageBoxVM, Task<Result>>? _showMessageBox;

    // The title follows the language, which can be changed from here
    private readonly SerialDisposable _title = new();

    private TopLevel? _topLevel;
    private IInputPane? _inputPane;

    // How wide the search is while it stands beside the title
    private const double CompactSearchWidth = 150;

    // The page that is open; null while the list of categories is shown
    private Control? _page;

    // What is on screen, or on its way there: the list of categories or a page
    private Control? _shown;
    private CancellationTokenSource? _transition;

    // The keys of the sections that are folded on the pages, which change the collection themselves
    private readonly ObservableCollection<string> _collapsedSections = [];
    private readonly ICollapsedSectionsRepository? _collapsedSectionsRepository;

    // The last save of a folded section. The next one waits for it, so they land in the order they were made
    private Task _collapsedSectionSaving = Task.CompletedTask;

    public SettingsView()
    {
        InitializeComponent();

        // The wide search is as wide as the header, which is known after the layout and changes with the screen
        HeaderArea.SizeChanged += (_, _) => ArrangeHeader();
    }

    /// <param name="showMessageBox">Asks the user a question over the settings.</param>
    public SettingsView(IServiceProvider services, Func<MessageBoxVM, Task<Result>> showMessageBox) : this()
    {
        _showMessageBox = showMessageBox;

        _appearance = new AppearanceControlVM(
            services.GetRequiredService<IAppearanceService>(),
            services.GetRequiredService<IThemeService>(),
            services.GetRequiredService<ILanguageService>());
        _behavior = new BehaviorControlVM(services.GetRequiredService<IBehaviorService>());
        _audio = new AudioControlVM(
            services.GetRequiredService<IAudioService>(),
            services.GetRequiredService<IMicrophoneTester>(),
            services.GetRequiredService<ISoundService>(),
            services.GetRequiredService<ISoundPackCatalog>());
        _storage = new StorageControlVM(services.GetRequiredService<IStorageService>());
        _debug = new DebugAndLogsControlVM(services.GetRequiredService<IDebugAndLogService>());
        _accounting = new AccountingControlVM(services.GetRequiredService<IAccountingService>(),
            services.GetRequiredService<IDatabaseSecurityService>());

        _about = new AboutControlVM(ApplicationDetails.Version, ApplicationDetails.SystemDescription);

        _accounting.ShowMessageBox.RegisterHandler(async context =>
            context.SetOutput(await showMessageBox(context.Input)));
        _storage.ShowMessageBox.RegisterHandler(async context =>
            context.SetOutput(await showMessageBox(context.Input)));
        _debug.ShowMessageBox.RegisterHandler(async context =>
            context.SetOutput(await showMessageBox(context.Input)));
        _debug.PickArchiveTarget.RegisterHandler(async context =>
            context.SetOutput(await PickArchiveTarget(context.Input)));

        AppearancePage.DataContext = _appearance;
        BehaviorPage.DataContext = _behavior;
        AudioPage.DataContext = _audio;
        StoragePage.DataContext = _storage;
        DebugPage.DataContext = _debug;
        AccountingPage.DataContext = _accounting;
        AboutPage.DataContext = _about;

        _appearance.LoadCommand.Execute().Subscribe();
        _behavior.LoadCommand.Execute().Subscribe();
        _audio.LoadCommand.Execute().Subscribe();
        _storage.LoadCommand.Execute().Subscribe();
        _debug.LoadCommand.Execute().Subscribe();
        _accounting.LoadCommand.Execute().Subscribe();

        _collapsedSectionsRepository = services.GetRequiredService<ICollapsedSectionsRepository>();
        _ = LoadCollapsedSectionsAsync(_collapsedSectionsRepository);

        _ = ShowPageAsync(null, "settings.title");
    }

    private async Task LoadCollapsedSectionsAsync(ICollapsedSectionsRepository repository)
    {
        foreach (var key in await repository.GetAsync())
            _collapsedSections.Add(key);
        _collapsedSections.CollectionChanged += OnCollapsedSectionsChanged;

        // Inherited by the pages, which open with their sections as they were left
        SettingsPage.SetCollapsedSections(this, _collapsedSections);
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
        await _collapsedSectionsRepository!.SetAsync(key, isCollapsed);
    }

    /// <summary>
    /// Lets go of the services the pages listen to, once the settings are closed for good.
    /// </summary>
    public void Release() => _audio?.Dispose();

    /// <summary>
    /// Raised when the user leaves the settings from the list of categories.
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Goes from a page back to the list of categories.
    /// </summary>
    /// <returns>False when the list is shown already, so there is nowhere to go back to.</returns>
    public bool TryGoBack()
    {
        if (_page != null)
        {
            _ = ShowPageAsync(null, "settings.title");
            return true;
        }

        // On the list of categories it first takes the search back
        if (!string.IsNullOrEmpty(SearchBox.Text))
        {
            SearchBox.Text = string.Empty;
            return true;
        }

        return false;
    }

    // The settings the pages show here, each with the page it is on. A tab of the desktop lists more
    // settings than its page has on Android, so only the ones a page marks with their key are searched.
    // Built on every search: the texts are the ones of the language at that moment
    private IReadOnlyList<SettingSearchResultVM> BuildSearchIndex()
    {
        var index = new List<SettingSearchResultVM>();

        foreach (var (tab, page) in (ReadOnlySpan<(ISettingVM?, SettingsPage)>)
                 [(_appearance, AppearancePage), (_behavior, BehaviorPage), (_audio, AudioPage),
                     (_storage, StoragePage), (_debug, DebugPage), (_accounting, AccountingPage), (_about, AboutPage)])
        {
            if (tab == null)
                continue;

            var shown = page.GetLogicalDescendants().OfType<Control>()
                .Select(SettingsPage.GetSearchKey)
                .ToHashSet();

            index.AddRange(tab.SearchEntries
                .Where(entry => shown.Contains(entry.Key))
                .Select(entry => new SettingSearchResultVM(tab, entry)));
        }

        return index;
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text;
        var isSearching = !string.IsNullOrWhiteSpace(query);
        IReadOnlyList<SettingSearchResultVM> results =
            isSearching ? SettingsSearch.Find(query, BuildSearchIndex(), result => result.Entry) : [];

        SearchResults.ItemsSource = results;
        CategoryList.IsVisible = !isSearching;
        SearchResults.IsVisible = results.Count > 0;
        NothingFound.IsVisible = isSearching && results.Count == 0;

        ArrangeHeader();
    }

    private void OnSearchFocusChanged(object? sender, RoutedEventArgs e) => ArrangeHeader();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _topLevel = TopLevel.GetTopLevel(this);
        _inputPane = _topLevel?.InputPane;
        if (_inputPane != null)
            _inputPane.StateChanged += OnInputPaneStateChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_inputPane != null)
            _inputPane.StateChanged -= OnInputPaneStateChanged;
        _inputPane = null;
        _topLevel = null;

        base.OnDetachedFromVisualTree(e);
    }

    // Android leaves the focus in a field whose keyboard was put away. The search gives it up then,
    // to the button next to it, which folds it back beside the title unless something is typed in it
    private void OnInputPaneStateChanged(object? sender, InputPaneStateEventArgs e)
    {
        if (e.NewState == InputPaneState.Closed && SearchBox.IsFocused)
            BackButton.Focus();
    }

    // A page has only its name in the header. The list of categories has the title and the search
    // beside it, and the search over the whole header while it is focused or typed in
    private void ArrangeHeader()
    {
        var isSearchShown = _page == null;
        var isSearchWide = isSearchShown && (SearchBox.IsFocused || !string.IsNullOrEmpty(SearchBox.Text));

        SearchBox.IsVisible = isSearchShown;
        SearchBox.Width = isSearchWide
            ? Math.Max(CompactSearchWidth, HeaderArea.Bounds.Width)
            : CompactSearchWidth;

        // The title ends where the search begins, and is not seen through it once that has widened
        TitleText.Margin = new Thickness(0, 0, isSearchShown ? CompactSearchWidth + 8 : 0, 0);
        TitleText.Opacity = isSearchWide ? 0 : 1;
    }

    private async void OnSearchResultClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not SettingSearchResultVM result)
            return;

        var (page, titleKey) = result.Tab switch
        {
            AppearanceControlVM => (AppearancePage, "settings.appearance.name"),
            BehaviorControlVM => (BehaviorPage, "settings.behavior.name"),
            AudioControlVM => (AudioPage, "settings.audio.name"),
            StorageControlVM => (StoragePage, "settings.storage.name"),
            DebugAndLogsControlVM => (DebugPage, "settings.debug.name"),
            AboutControlVM => (AboutPage, "settings.about.name"),
            _ => (AccountingPage, "settings.accounting.name")
        };

        await ShowPageAsync(page, titleKey);

        // The setting is scrolled to and flashed once the page has slid in and is laid out
        Dispatcher.UIThread.Post(() => page.ScrollTo(result.Entry.Key), DispatcherPriority.Loaded);
    }

    // A page slides in over the list of categories and back out of it
    private async Task ShowPageAsync(Control? page, string titleKey)
    {
        var from = _shown;
        var to = page ?? Categories;

        _page = page;
        _shown = to;
        _title.Disposable = TitleText.Bind(TextBlock.TextProperty, Strings.Observe(titleKey));

        ArrangeHeader();

        // A transition that is still running gives way to this one
        _transition?.Cancel();
        _transition = null;

        if (from == null || ReferenceEquals(from, to))
        {
            ShowOnlyCurrent();
            return;
        }

        var transition = _transition = new CancellationTokenSource();
        try
        {
            await new ScreenSlide().Start(from, to, page != null, transition.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            // Unless a newer transition has taken over, which leaves the screens in order itself
            if (ReferenceEquals(_transition, transition))
            {
                _transition = null;
                ShowOnlyCurrent();
            }

            transition.Dispose();
        }
    }

    private void ShowOnlyCurrent()
    {
        foreach (var screen in (Control[])[Categories, AppearancePage, BehaviorPage, AudioPage, StoragePage, DebugPage, AccountingPage, AboutPage])
            screen.IsVisible = ReferenceEquals(screen, _shown);
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (!TryGoBack())
            CloseRequested?.Invoke();
    }

    private void OnAppearanceClick(object? sender, RoutedEventArgs e) =>
        _ = ShowPageAsync(AppearancePage, "settings.appearance.name");

    private void OnBehaviorClick(object? sender, RoutedEventArgs e) =>
        _ = ShowPageAsync(BehaviorPage, "settings.behavior.name");

    private void OnAudioClick(object? sender, RoutedEventArgs e) =>
        _ = ShowPageAsync(AudioPage, "settings.audio.name");

    // What is kept changes while the application runs, so the page shows it as it is now
    private void OnStorageClick(object? sender, RoutedEventArgs e)
    {
        _storage?.LoadCommand.Execute().Subscribe();
        _ = ShowPageAsync(StoragePage, "settings.storage.name");
    }

    private void OnDebugClick(object? sender, RoutedEventArgs e)
    {
        // The size of the files changes all the time
        _debug?.LoadCommand.Execute().Subscribe();
        _ = ShowPageAsync(DebugPage, "settings.debug.name");
    }

    // Through the documents of the system: the folder of the journal is the application's own
    private async Task<Stream?> PickArchiveTarget(string fileName)
    {
        var storage = _topLevel?.StorageProvider ?? TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null)
            return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = fileName });
        return file == null ? null : await file.OpenWriteAsync();
    }

    private void OnAccountingClick(object? sender, RoutedEventArgs e) =>
        _ = ShowPageAsync(AccountingPage, "settings.accounting.name");

    private void OnAboutClick(object? sender, RoutedEventArgs e) =>
        _ = ShowPageAsync(AboutPage, "settings.about.name");

    // Opened by the system, in the browser of the user
    private void OnRepositoryClick(object? sender, RoutedEventArgs e)
    {
        if (_about != null)
            _ = _topLevel?.Launcher.LaunchUriAsync(_about.RepositoryUrl);
    }

    private async void OnResetClick(object? sender, RoutedEventArgs e)
    {
        if (_showMessageBox == null || _appearance == null || _behavior == null || _accounting == null)
            return;

        var answer = await _showMessageBox(new MessageBoxVM(Icon.Warning, Buttons.YesNo,
            Strings.Get("settings.reset.question"), Strings.Get("common.warning")));
        if (answer != Result.Yes)
            return;

        await _accounting.ResetAllAsync();
        await _behavior.ResetAllAsync();
        if (_audio != null)
            await _audio.ResetAllAsync();
        if (_storage != null)
            await _storage.ResetAllAsync();
        if (_debug != null)
            await _debug.ResetAllAsync();
        await _appearance.ResetAllAsync();
    }
}
