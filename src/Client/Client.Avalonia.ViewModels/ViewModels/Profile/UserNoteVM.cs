using System;
using System.Reactive.Linq;
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

    public string Text
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value ?? string.Empty);
            IsSaved = false;
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

    private async Task SaveAsync()
    {
        IsBusy = true;
        IsSaved = false;
        Error = null;
        var text = string.IsNullOrWhiteSpace(Text) ? string.Empty : Text;
        try
        {
            await _repository.SetAsync(_coordinatorInstanceId, _userId, text);
            SavedText = text;
            if (string.IsNullOrWhiteSpace(Text))
                Text = string.Empty;
            IsSaved = Text == SavedText;
        }
        catch (Exception)
        {
            Error = Strings.Get("profile.note.saveFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        LoadCommand.Dispose();
        SaveCommand.Dispose();
    }
}
