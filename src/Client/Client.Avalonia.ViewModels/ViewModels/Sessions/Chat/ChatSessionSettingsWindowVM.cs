using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Avalonia.Localization;
using Client.Core.Coordinator;
using Client.Core.Sessions;
using Client.Services;
using ReactiveUI;
using Shared.Enums;
using Shared.Validation;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Chat;

public sealed class ChatSessionSettingsWindowVM : ReactiveObject, IDisposable
{
    private readonly ChatSessionModel _session;
    private readonly ReactiveCommand<(int SessionId, string Name, string? Description), RequestResult> _changeSettings;
    private readonly CompositeDisposable _disposables = new();
    private readonly ObservableAsPropertyHelper<bool> _isBusy;

    public int SessionId => _session.Id;
    public string Name { get; set => this.RaiseAndSetIfChanged(ref field, value); }
    public string Description { get; set => this.RaiseAndSetIfChanged(ref field, value); }
    public int DescriptionMaxLength => Limits.SessionDescriptionMaxLength;
    public string? DescriptionError { get; private set => this.RaiseAndSetIfChanged(ref field, value); }
    public string? NameError { get; private set => this.RaiseAndSetIfChanged(ref field, value); }
    public string? GeneralError { get; private set => this.RaiseAndSetIfChanged(ref field, value); }
    public bool IsBusy => _isBusy.Value;
    public Interaction<RxVoid, RxVoid> CloseWindow { get; } = new();
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }

    public ChatSessionSettingsWindowVM(ChatSessionModel session,
        ReactiveCommand<(int SessionId, string Name, string? Description), RequestResult> changeSettings)
    {
        _session = session;
        _changeSettings = changeSettings;
        Name = session.Name;
        Description = session.Description ?? string.Empty;
        var canSave = this.WatchProperties(nameof(Name), nameof(Description))
            .Select(vm => SessionRules.ValidateName(vm.Name) == null
                && SessionRules.ValidateDescription(vm.Description) == null
                && (vm.Name.Trim() != session.Name || NormalizeDescription(vm.Description) != NormalizeDescription(session.Description)));
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, canSave);
        CloseCommand = ReactiveCommand.CreateFromTask(async () => { await CloseWindow.Handle(RxVoid.Default); },
            SaveCommand.IsExecuting.Select(busy => !busy));
        _isBusy = SaveCommand.IsExecuting.ToProperty(this, nameof(IsBusy));
        _disposables.Add(this.WatchProperty(nameof(Name), vm => vm.Name).Subscribe(name =>
        {
            NameError = SessionRules.ValidateName(name);
            GeneralError = null;
        }));
        _disposables.Add(this.WatchProperty(nameof(Description), vm => vm.Description).Subscribe(description =>
        {
            DescriptionError = SessionRules.ValidateDescription(description);
            GeneralError = null;
        }));
    }

    private async Task SaveAsync()
    {
        GeneralError = null;
        var result = await _changeSettings.Execute((SessionId, Name.Trim(), NormalizeDescription(Description)));
        switch (result)
        {
            case { IsSuccess: true }:
                await CloseWindow.Handle(RxVoid.Default);
                break;
            case { Error: ErrorCode.NameTaken }:
                NameError = Strings.Get("createSession.name.taken");
                break;
            case { Error: ErrorCode.InvalidValue }:
                NameError = Strings.Get("profile.name.notAccepted");
                break;
            case { Error: ErrorCode.SessionNotFound }:
                GeneralError = Strings.Get("sessionSettings.sessionGone");
                break;
            case { Error: ErrorCode.TimedOut }:
                GeneralError = Strings.Get("profile.name.timedOut");
                break;
            case { IsRejected: true }:
                break;
            default:
                GeneralError = Strings.Format("sessionSettings.notSaved", ("reason", result));
                break;
        }
    }

    private static string? NormalizeDescription(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Dispose()
    {
        _disposables.Dispose();
        _isBusy.Dispose();
        SaveCommand.Dispose();
        CloseCommand.Dispose();
    }
}
