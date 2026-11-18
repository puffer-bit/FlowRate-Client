using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Client.Application.Ports;
using Client.Core.Settings;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Settings.Pages;

public sealed class ReleaseHistoryVM : ReactiveObject, IDisposable
{
    // Shown at first and added by each Show more: a page that opens fast and still reaches far back
    public const int PageSize = 10;

    private readonly IReleaseNotesSource _source;
    private readonly ObservableCollection<ReleaseNote> _shown = [];
    private IReadOnlyList<ReleaseNote> _all = [];
    private readonly BehaviorControlVM? _updatePreferences;
    private bool _activated;
    private CancellationTokenSource? _request;
    private bool _loaded;
    private bool _disposed;

    public bool IsDev
    {
        get;
        set
        {
            if (field == value)
                return;
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(ChannelIndex));
            _loaded = false;
            if (_activated)
                _ = LoadAsync(false);
        }
    }

    public int ChannelIndex
    {
        get => IsDev ? 1 : 0;
        set { if (value is 0 or 1) IsDev = value == 1; }
    }

    /// <summary>The newest releases, a page more after each <see cref="ShowMoreCommand"/>.</summary>
    public IReadOnlyList<ReleaseNote> Releases => _shown;

    /// <summary>Releases loaded but not shown yet.</summary>
    public int HiddenCount
    {
        get;
        private set
        {
            if (field == value)
                return;
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasMore));
        }
    }

    public bool HasMore => HiddenCount > 0;

    public bool IsLoading
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool HasError
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsEmpty
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> RefreshCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ShowMoreCommand { get; }

    public ReleaseHistoryVM(IReleaseNotesSource source, BehaviorControlVM? updatePreferences = null)
    {
        _source = source;
        _updatePreferences = updatePreferences;
        if (_updatePreferences != null)
        {
            IsDev = _updatePreferences.UpdateChannelIndex == (int)UpdateChannel.Dev;
            _updatePreferences.PropertyChanged += OnUpdatePreferencesChanged;
        }
        RefreshCommand = ReactiveCommand.CreateFromTask(ReloadAsync);
        ShowMoreCommand = ReactiveCommand.Create(ShowMore);
    }

    public void ShowMore()
    {
        // Added to the end, so the releases already shown keep their views and what is expanded in them
        foreach (var release in _all.Skip(_shown.Count).Take(PageSize))
            _shown.Add(release);
        HiddenCount = _all.Count - _shown.Count;
    }

    private void Show(IReadOnlyList<ReleaseNote> releases)
    {
        _all = releases;
        _shown.Clear();
        ShowMore();
    }

    private void OnUpdatePreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BehaviorControlVM.UpdateChannelIndex))
            IsDev = _updatePreferences!.UpdateChannelIndex == (int)UpdateChannel.Dev;
    }

    public Task EnsureLoadedAsync()
    {
        _activated = true;
        return _loaded || IsLoading || _disposed ? Task.CompletedTask : LoadAsync(false);
    }

    public Task ReloadAsync()
    {
        _activated = true;
        return LoadAsync(true);
    }

    private async Task LoadAsync(bool forceRefresh)
    {
        if (_disposed)
            return;
        _request?.Cancel();
        using var request = new CancellationTokenSource();
        _request = request;
        IsLoading = true;
        HasError = false;
        IsEmpty = false;
        Show([]);
        try
        {
            var releases = await _source.GetAsync(IsDev ? UpdateChannel.Dev : UpdateChannel.Release, forceRefresh, request.Token);
            if (request.IsCancellationRequested)
                return;
            Show(releases.OrderByDescending(release => release.PublishedAt)
                .Select((release, index) => new ReleaseNote(release) { IsLatest = index == 0, IsExpanded = index == 0 })
                .ToArray());
            _loaded = true;
            IsEmpty = _all.Count == 0;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or FormatException)
        {
            if (!request.IsCancellationRequested)
                HasError = true;
        }
        finally
        {
            if (ReferenceEquals(_request, request))
            {
                _request = null;
                IsLoading = false;
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_updatePreferences != null)
            _updatePreferences.PropertyChanged -= OnUpdatePreferencesChanged;
        _request?.Cancel();
        RefreshCommand.Dispose();
        ShowMoreCommand.Dispose();
    }
}
