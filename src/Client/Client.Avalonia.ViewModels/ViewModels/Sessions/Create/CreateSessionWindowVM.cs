using Client.Services;
using Client.Avalonia.Localization;
using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using Shared.Enums;
using Shared.Enums.WebRTC;
using Shared.Validation;
using Client.Core.Sessions;
using Client.Application.Coordinator;

namespace Client.ViewModels.Sessions.Create;

/// <summary>
/// A session type the dialog offers, shown as a selectable card.
/// </summary>
public sealed class SessionTypeOptionVM : ReactiveObject
{
    public SessionType Type { get; }
    public string Title { get; }
    public string Description { get; }
    public int DefaultCapacity { get; }

    public bool IsSelected
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public SessionTypeOptionVM(SessionType type, string title, string description, int defaultCapacity)
    {
        Type = type;
        Title = title;
        Description = description;
        DefaultCapacity = defaultCapacity;
    }
}

public sealed class CreateSessionWindowVM : ReactiveObject, IDisposable
{
    private readonly ICoordinatorSession _coordinatorSession;
    private readonly int? _categoryId;
    private readonly CompositeDisposable _disposables = new();
    private readonly ObservableAsPropertyHelper<bool> _isBusy;

    // Voice sessions have no options and no server support yet, so they are not offered
    public SessionTypeOptionVM HybridOption { get; } =
        new(SessionType.Hybrid, Strings.Get("session.type.hybrid"), Strings.Get("session.type.hybrid.description"), 8);

    public SessionTypeOptionVM ChatOption { get; } =
        new(SessionType.Chat, Strings.Get("session.type.chat"), Strings.Get("session.type.chat.description"), 50);

    public SessionTypeOptionVM SelectedType
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsChatSelected));
        }
    }

    public bool IsChatSelected => SelectedType.Type == SessionType.Chat;

    public string Name
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    /// <summary>
    /// Null while the number box is empty. Decimal because that is what the number box binds to.
    /// </summary>
    public decimal? Capacity
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string Description
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

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

    public string? DescriptionError
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

    public int DescriptionMaxLength => Limits.SessionDescriptionMaxLength;
    public decimal CapacityMin => SessionRules.CapacityMin;
    public decimal CapacityMax => SessionRules.CapacityMax;
    public string CapacityHint => SessionRules.CapacityHint;

    public Interaction<RxVoid, RxVoid> CloseWindow { get; } = new();

    public ReactiveCommand<SessionTypeOptionVM, RxVoid> SelectTypeCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CreateCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloseCommand { get; }

    /// <param name="categoryId">The category the session is created in, or null for none.</param>
    public CreateSessionWindowVM(ICoordinatorSession coordinatorSession, SessionType initialType = SessionType.Hybrid,
        int? categoryId = null)
    {
        _coordinatorSession = coordinatorSession;
        _categoryId = categoryId;

        SelectedType = initialType == SessionType.Chat ? ChatOption : HybridOption;
        SelectedType.IsSelected = true;
        Capacity = SelectedType.DefaultCapacity;

        var isValid = this.WatchProperties(nameof(Name), nameof(Capacity), nameof(Description), nameof(SelectedType))
            .Select(vm =>
                SessionRules.ValidateName(vm.Name) == null
                && SessionRules.ValidateCapacity(vm.Capacity) == null
                && (vm.SelectedType.Type != SessionType.Chat || ValidateDescription(vm.Description) == null));

        CreateCommand = ReactiveCommand.CreateFromTask(CreateAsync, isValid);
        SelectTypeCommand = ReactiveCommand.Create<SessionTypeOptionVM>(SelectType, CreateCommand.IsExecuting.Select(busy => !busy));
        CloseCommand = ReactiveCommand.CreateFromTask(async () => { await CloseWindow.Handle(RxVoid.Default); },
            CreateCommand.IsExecuting.Select(busy => !busy));

        _isBusy = CreateCommand.IsExecuting.ToProperty(this, nameof(IsBusy));

        // The name has no error until it is first edited, an empty untouched form is not a mistake
        _disposables.Add(this.WatchProperty(nameof(Name), vm => vm.Name).Skip(1).Subscribe(name =>
        {
            NameError = SessionRules.ValidateName(name);
            GeneralError = null;
        }));
        _disposables.Add(this.WatchProperty(nameof(Capacity), vm => vm.Capacity)
            .Subscribe(capacity => CapacityError = SessionRules.ValidateCapacity(capacity)));
        _disposables.Add(this.WatchProperty(nameof(Description), vm => vm.Description)
            .Subscribe(description => DescriptionError = ValidateDescription(description)));
    }

    private void SelectType(SessionTypeOptionVM option)
    {
        if (option == SelectedType)
            return;

        var previous = SelectedType;
        previous.IsSelected = false;
        option.IsSelected = true;
        SelectedType = option;
        GeneralError = null;

        // Follow the default of the new type unless the user picked a number of their own
        if (Capacity == previous.DefaultCapacity)
            Capacity = option.DefaultCapacity;
    }

    private async Task CreateAsync()
    {
        await Task.Delay(1500);
        
        NameError = SessionRules.ValidateName(Name);
        GeneralError = null;

        if (NameError != null || Capacity is not { } capacity)
            return;

        NewSessionOptions options = SelectedType.Type switch
        {
            SessionType.Chat => new NewChatSessionOptions(
                Description: string.IsNullOrWhiteSpace(Description) ? null : Description.Trim()),
            _ => new NewHybridSessionOptions(WebRTCEngine.GStreamer)
        };

        var session = new NewSessionModel(Name.Trim(), _coordinatorSession.GetCurrentUser().Id,
            (int)capacity, options, _categoryId);

        var result = await _coordinatorSession.CreateSession(session);

        switch (result)
        {
            case { IsSuccess: true }:
                await CloseWindow.Handle(RxVoid.Default);
                break;
            case { Error: ErrorCode.NameTaken }:
                NameError = Strings.Get("createSession.name.taken");
                break;
            case { Error: ErrorCode.InvalidValue }:
                GeneralError = Strings.Get("createSession.notAccepted");
                break;
            case { Error: ErrorCode.TimedOut }:
                GeneralError = Strings.Get("profile.name.timedOut");
                break;
            // The main window tells why; the form stays filled in for another try
            case { IsRejected: true }:
                break;
            default:
                GeneralError = Strings.Format("createSession.notCreated", ("reason", result));
                break;
        }
    }

    private static string? ValidateDescription(string? description)
    {
        if (string.IsNullOrEmpty(description))
            return null;

        if (description.Trim().Length > Limits.SessionDescriptionMaxLength)
            return Strings.Format("profile.name.tooLong", ("limit", Limits.SessionDescriptionMaxLength));

        return description.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))
            ? Strings.Get("createSession.description.controlCharacters")
            : null;
    }

    public void Dispose()
    {
        _disposables.Dispose();
        _isBusy.Dispose();
        SelectTypeCommand.Dispose();
        CreateCommand.Dispose();
        CloseCommand.Dispose();
    }
}
