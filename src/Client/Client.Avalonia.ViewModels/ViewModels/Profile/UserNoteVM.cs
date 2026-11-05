using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Client.Application.Ports.Repositories;
using Client.Avalonia.Localization;
using Client.Services;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Profile;

/// <summary>A local note whose target stays fixed even if the coordinator connection changes.</summary>
public sealed class UserNoteVM : ReactiveObject, IDisposable
{
    private readonly IUserNotesRepository _repository;
    private readonly string _coordinatorInstanceId;
    private readonly int _userId;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private CancellationTokenSource? _pendingSave;
    private bool _disposed;

    public string Text
    {
        get;
        set
        {
            value ??= string.Empty;
            if (field == value)
                return;
            this.RaiseAndSetIfChanged(ref field, value);
            IsSaved = false;
            if (IsLoaded && !_disposed)
            {
                _pendingSave?.Cancel();
                _pendingSave?.Dispose();
                _pendingSave = new CancellationTokenSource();
                _ = AutoSaveAsync(_pendingSave.Token);
            }
        }
    } = string.Empty;

    private string SavedText
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public bool IsLoaded
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsBusy
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsSaved
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string? Error
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }

    public UserNoteVM(IUserNotesRepository repository, string coordinatorInstanceId, int userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinatorInstanceId);
        _repository = repository;
        _coordinatorInstanceId = coordinatorInstanceId;
        _userId = userId;

        LoadCommand = ReactiveCommand.CreateFromTask(LoadAsync,
            this.WatchProperties(nameof(IsLoaded), nameof(IsBusy)).Select(vm => !vm.IsLoaded && !vm.IsBusy));
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync,
            this.WatchProperties(nameof(Text), nameof(SavedText), nameof(IsLoaded), nameof(IsBusy))
                .Select(vm => vm.IsLoaded && !vm.IsBusy && vm.Text != vm.SavedText));
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        Error = null;
        try
        {
            Text = await _repository.GetAsync(_coordinatorInstanceId, _userId) ?? string.Empty;
            SavedText = Text;
            IsLoaded = true;
        }
        catch (Exception)
        {
            Error = Strings.Get("profile.note.loadFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AutoSaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
            await SaveAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task SaveAsync()
    {
        await _saveGate.WaitAsync();
        try
        {
            if (!IsLoaded || Text == SavedText)
                return;
            IsBusy = true;
            IsSaved = false;
            Error = null;
            // Serialize writes and include text entered while an earlier version was being saved.
            while (Text != SavedText)
            {
                var draft = Text;
                var text = string.IsNullOrWhiteSpace(draft) ? string.Empty : draft;
                await _repository.SetAsync(_coordinatorInstanceId, _userId, text);
                SavedText = text;
                if (Text == draft && text.Length == 0)
                    Text = string.Empty;
            }
            IsSaved = true;
        }
        catch (Exception)
        {
            Error = Strings.Get("profile.note.saveFailed");
        }
        finally
        {
            IsBusy = false;
            _saveGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pendingSave?.Cancel();
        _pendingSave?.Dispose();
        // Closing the profile must also save text whose debounce has not elapsed yet.
        if (IsLoaded && Text != SavedText)
            _ = SaveAsync();
        LoadCommand.Dispose();
        SaveCommand.Dispose();
    }
}
