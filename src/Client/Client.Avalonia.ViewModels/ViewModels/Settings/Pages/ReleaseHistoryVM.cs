using System;
using System.Collections.Generic;
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
    private readonly IReleaseNotesSource _source;
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

    public IReadOnlyList<ReleaseNote> Releases
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = [];

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
        Releases = [];
        try
        {
            var releases = await _source.GetAsync(IsDev ? UpdateChannel.Dev : UpdateChannel.Release, forceRefresh, request.Token);
            if (request.IsCancellationRequested)
                return;
            Releases = releases.OrderByDescending(release => release.PublishedAt).Take(6)
                .Select((release, index) => new ReleaseNote(release) { IsExpanded = index == 0 }).ToArray();
            _loaded = true;
            IsEmpty = Releases.Count == 0;
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
    }
}
