using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Client.Avalonia.Localization;
using Client.Services;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

/// <summary>A searchable page of saved settings data, following additions and deletions.</summary>
public sealed class SettingsListPageVM<T> : ReactiveObject, IDisposable
{
    public const int PageSize = 20;
    private readonly ObservableCollection<T> _source;
    private readonly Func<T, string, bool> _matches;
    private int _currentPage = 1;
    private int _filteredCount;

    public ObservableCollection<T> Items { get; } = [];
    public string? Filter
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            _currentPage = 1;
            Rebuild();
        }
    }

    public int TotalCount => _source.Count;
    public int CurrentPage => _currentPage;
    public int PageCount => Math.Max(1, (_filteredCount + PageSize - 1) / PageSize);
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < PageCount;
    public bool HasMultiplePages => PageCount > 1;
    public bool HasItems => TotalCount > 0;
    public bool HasNoMatches => HasItems && _filteredCount == 0;
    public string CountText => Strings.Format("serverSettings.users.count", ("count", TotalCount));
    public string PageText => Strings.Format("serverSettings.users.page", ("page", CurrentPage), ("pages", PageCount));
    public string RangeText => Strings.Format("serverSettings.users.range",
        ("first", _filteredCount == 0 ? 0 : (CurrentPage - 1) * PageSize + 1),
        ("last", Math.Min(CurrentPage * PageSize, _filteredCount)), ("count", _filteredCount));
    public ReactiveCommand<RxVoid, RxVoid> PreviousPageCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NextPageCommand { get; }

    public SettingsListPageVM(ObservableCollection<T> source, Func<T, string, bool> matches)
    {
        _source = source;
        _matches = matches;
        PreviousPageCommand = ReactiveCommand.Create(() => ChangePage(-1),
            this.WatchProperty(nameof(HasPreviousPage), vm => vm.HasPreviousPage));
        NextPageCommand = ReactiveCommand.Create(() => ChangePage(1),
            this.WatchProperty(nameof(HasNextPage), vm => vm.HasNextPage));
        _source.CollectionChanged += OnCollectionChanged;
        Strings.LanguageChanged += OnLanguageChanged;
        Rebuild();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void ChangePage(int offset)
    {
        _currentPage = Math.Clamp(CurrentPage + offset, 1, PageCount);
        Rebuild();
    }

    private void Rebuild()
    {
        var filter = Filter?.Trim() ?? string.Empty;
        var matching = _source.Where(item => filter.Length == 0 || _matches(item, filter)).ToList();
        _filteredCount = matching.Count;
        _currentPage = Math.Clamp(CurrentPage, 1, PageCount);
        Items.Clear();
        foreach (var item in matching.Skip((CurrentPage - 1) * PageSize).Take(PageSize))
            Items.Add(item);
        foreach (var name in new[] { nameof(TotalCount), nameof(CurrentPage), nameof(PageCount),
                     nameof(HasPreviousPage), nameof(HasNextPage), nameof(HasMultiplePages),
                     nameof(HasItems), nameof(HasNoMatches) })
            this.RaisePropertyChanged(name);
        OnLanguageChanged();
    }

    private void OnLanguageChanged()
    {
        this.RaisePropertyChanged(nameof(CountText));
        this.RaisePropertyChanged(nameof(PageText));
        this.RaisePropertyChanged(nameof(RangeText));
    }

    public void Dispose()
    {
        _source.CollectionChanged -= OnCollectionChanged;
        Strings.LanguageChanged -= OnLanguageChanged;
        PreviousPageCommand.Dispose();
        NextPageCommand.Dispose();
    }
}
