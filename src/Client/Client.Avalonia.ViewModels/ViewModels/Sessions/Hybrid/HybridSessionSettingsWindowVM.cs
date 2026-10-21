using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using Shared.Enums;
using Client.Avalonia.Localization;
using Client.Core.Sessions.Hybrid;
using Client.Services;

namespace Client.ViewModels.Sessions.Hybrid;

/// <summary>
/// The dialog that changes the settings of a hybrid session. The saved session comes back to the list
/// with the change the server tells every client about.
/// </summary>
public sealed class HybridSessionSettingsWindowVM : ReactiveObject, IDisposable
{
    private readonly HybridSessionModel _session;
    private readonly ReactiveCommand<(int SessionId, string Name, int Capacity, bool IsHostMustApprove),
        ChangeSessionSettingsResult> _changeSettingsCommand;
    private readonly CompositeDisposable _disposables = new();
    private readonly ObservableAsPropertyHelper<bool> _isBusy;

    public int SessionId => _session.Id;

    public string Name
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// Null while the number box is empty. Decimal because that is what the number box binds to.
    /// </summary>
    public decimal? Capacity
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// Users wait for the host to let them in.
    /// </summary>
    public bool IsApprovalRequested
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string? NameError
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string? CapacityError
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// A failure that belongs to no single field, shown above the buttons.
    /// </summary>
    public string? GeneralError
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsBusy => _isBusy.Value;

    public decimal CapacityMin => SessionRules.CapacityMin;
    public decimal CapacityMax => SessionRules.CapacityMax;
    public string CapacityHint => SessionRules.CapacityHint;

    public Interaction<RxVoid, RxVoid> CloseWindow { get; } = new();

    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }

    public HybridSessionSettingsWindowVM(HybridSessionModel session,
        ReactiveCommand<(int SessionId, string Name, int Capacity, bool IsHostMustApprove),
            ChangeSessionSettingsResult> changeSettingsCommand)
    {
        _session = session;
        _changeSettingsCommand = changeSettingsCommand;
        Name = session.Name;
        Capacity = session.Capacity;
        IsApprovalRequested = session.IsApprovalRequested;

        var hasChanges = this.WatchProperties(nameof(Name), nameof(Capacity), nameof(IsApprovalRequested))
            .Select(vm => SessionRules.ValidateName(vm.Name) == null
                && SessionRules.ValidateCapacity(vm.Capacity) == null
                && (vm.Name.Trim() != _session.Name
                    || vm.Capacity != _session.Capacity
                    || vm.IsApprovalRequested != _session.IsApprovalRequested));

        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, hasChanges);
        CloseCommand = ReactiveCommand.CreateFromTask(async () => { await CloseWindow.Handle(RxVoid.Default); },
            SaveCommand.IsExecuting.Select(busy => !busy));

        _isBusy = SaveCommand.IsExecuting.ToProperty(this, nameof(IsBusy));

        _disposables.Add(this.WatchProperty(nameof(Name), vm => vm.Name)
            .Subscribe(name =>
            {
                NameError = SessionRules.ValidateName(name);
                GeneralError = null;
            }));
        _disposables.Add(this.WatchProperty(nameof(Capacity), vm => vm.Capacity)
            .Subscribe(capacity => CapacityError = SessionRules.ValidateCapacity(capacity)));
    }

    private async Task SaveAsync()
    {
        GeneralError = null;

        if (Capacity is not { } capacity)
            return;

        var result = await _changeSettingsCommand.Execute((_session.Id, Name.Trim(), (int)capacity, IsApprovalRequested));

        switch (result)
        {
            case ChangeSessionSettingsResult.NoError:
                await CloseWindow.Handle(RxVoid.Default);
                break;
            case ChangeSessionSettingsResult.NameAlreadyUsed:
                NameError = Strings.Get("createSession.name.taken");
                break;
            case ChangeSessionSettingsResult.WrongCapacity:
                CapacityError = Strings.Get("sessionSettings.capacity.notAccepted");
                break;
            case ChangeSessionSettingsResult.SessionNotExists:
                GeneralError = Strings.Get("sessionSettings.sessionGone");
                break;
            case ChangeSessionSettingsResult.TimedOut:
                GeneralError = Strings.Get("profile.name.timedOut");
                break;
            // The main window tells why; the form stays filled in for another try
            case ChangeSessionSettingsResult.Rejected:
                break;
            default:
                GeneralError = Strings.Format("sessionSettings.notSaved", ("reason", result));
                break;
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
        _isBusy.Dispose();
        SaveCommand.Dispose();
        CloseCommand.Dispose();
    }
}
