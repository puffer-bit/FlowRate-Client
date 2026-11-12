using System;
using System.Collections.ObjectModel;
using Client.Core.Users;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Application.Coordinator;
using Client.Avalonia.Localization;
using Client.Core.Sessions;
using Client.Services;
using ReactiveUI;
using Shared.Enums;
using Shared.Enums.WebRTC;
using Shared.Validation;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.Create;

public sealed class CreateSessionWindowVM : ReactiveObject, IDisposable
{
    private readonly ICoordinatorSession _coordinatorSession;
    private readonly int? _categoryId;
    private readonly ChatSessionModel? _editingGroup;
    private int _searchVersion;
    private bool _disposed;
    public SessionModel? CreatedSession { get; private set; }
    public bool IsPrivateMode { get; }
    public bool IsEditingGroup => _editingGroup != null;
    public bool ShowServerOptions => !IsPrivateMode;
    public bool ShowName => !IsPrivateMode || IsGroup;
    public bool ShowCapacity => !IsPrivateMode && !IsChatSelected;
    public bool ShowDescription => !IsPrivateMode && IsChatSelected;
    public bool ShowOwner => IsEditingGroup;
    public string BusyText => Strings.Get(IsEditingGroup ? "common.saving" : "createSession.creating");
    public string SubmitText => Strings.Get(IsEditingGroup ? "common.save" : "createSession.create");
    public string DialogTitle => Strings.Get(IsEditingGroup ? "privateChats.editGroup" : "createSession.title");
    public ObservableCollection<ChatMemberChoiceVM> SearchResults { get; } = new();
    public ObservableCollection<ChatMemberChoiceVM> SelectedMembers { get; } = new();
    public ReactiveCommand<ChatMemberChoiceVM, RxVoid> ToggleMemberCommand { get; }
    public ChatMemberChoiceVM? SelectedOwner { get; set { this.RaiseAndSetIfChanged(ref field, value); } }
    public ReactiveCommand<RxVoid, RxVoid> SelectDirectCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SelectGroupCommand { get; }
    public bool IsGroup
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            if (!value && !IsEditingGroup)
                while (SelectedMembers.Count > 1) SelectedMembers.RemoveAt(SelectedMembers.Count - 1);
            RefreshMemberSelection();
            this.RaisePropertyChanged(nameof(ShowName));
        }
    }
    public string SearchQuery { get; set { this.RaiseAndSetIfChanged(ref field, value); } } = "";
    public string SelectedCountText => Strings.Format("privateChats.selectedCount", ("count", SelectedMembers.Count));
    public bool HasSelectedMembers => SelectedMembers.Count > 0;
    public bool IsSearching { get; private set { this.RaiseAndSetIfChanged(ref field, value); } }
    public bool ShowNoSearchResults => !IsSearching && SearchResults.Count == 0;

    private bool ValidPrivateMembers => IsEditingGroup
        ? SelectedMembers.Count is >= 1 and <= 100 && SelectedOwner != null && SelectedMembers.Contains(SelectedOwner)
        : IsGroup ? SelectedMembers.Count is >= 1 and < 100 : SelectedMembers.Count == 1;

    private void ToggleMember(ChatMemberChoiceVM choice)
    {
        var existing = SelectedMembers.FirstOrDefault(member => member.Id == choice.Id);
        if (existing != null)
        {
            SelectedMembers.Remove(existing);
            if (SelectedOwner == existing) SelectedOwner = null;
        }
        else
        {
            if (!IsGroup && !IsEditingGroup) SelectedMembers.Clear();
            if (SelectedMembers.Count >= (IsEditingGroup ? 100 : 99)) return;
            SelectedMembers.Add(choice);
        }
        RefreshMemberSelection();
    }

    private void RefreshMemberSelection()
    {
        foreach (var row in SearchResults)
        {
            row.IsSelected = SelectedMembers.Any(member => member.Id == row.Id);
            row.CanToggle = row.IsSelected || !IsGroup || SelectedMembers.Count < (IsEditingGroup ? 100 : 99);
        }
        this.RaisePropertyChanged(nameof(SelectedCountText));
        this.RaisePropertyChanged(nameof(HasSelectedMembers));
    }

    private async Task SearchMembersAsync(string query)
    {
        var version = ++_searchVersion;
        IsSearching = true;
        this.RaisePropertyChanged(nameof(ShowNoSearchResults));
        var users = await _coordinatorSession.SearchUsers(query, 20);
        if (_disposed || version != _searchVersion) return;
        SearchResults.Clear();
        foreach (var user in users.Where(user => IsEditingGroup || user.Id != _coordinatorSession.GetCurrentUser().Id))
            SearchResults.Add(new ChatMemberChoiceVM(user.Id, user.DisplayName ?? user.Username)
                { Username = user.Username, AvatarUrl = user.AvatarUrl });
        RefreshMemberSelection();
        IsSearching = false;
        this.RaisePropertyChanged(nameof(ShowNoSearchResults));
    }

    public async Task InitializeMembersAsync()
    {
        if (_editingGroup != null)
        {
            var users = await _coordinatorSession.GetUsers(_editingGroup.MemberIds);
            if (_disposed) return;
            foreach (var id in _editingGroup.MemberIds)
                SelectedMembers.Add(new ChatMemberChoiceVM(id, users.TryGetValue(id, out var user) ? user.DisplayName ?? user.Username : id.ToString()));
            SelectedOwner = SelectedMembers.FirstOrDefault(member => member.Id == _editingGroup.AdminUserId);
            RefreshMemberSelection();
        }
        await SearchMembersAsync("");
    }

    private readonly CompositeDisposable _disposables = new();
    private readonly ObservableAsPropertyHelper<bool> _isBusy;

    // Voice sessions have no options and no server support yet, so they are not offered
    public SessionTypeOptionVM HybridOption { get; } =
        new(SessionType.Hybrid, Strings.Get("session.type.hybrid"), Strings.Get("session.type.hybrid.description"), 8);

    public SessionTypeOptionVM ChatOption { get; } =
        new(SessionType.Chat, Strings.Get("session.type.chat"), Strings.Get("session.type.chat.description"), Limits.SessionCapacityUnlimited);

    public SessionTypeOptionVM SelectedType
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsChatSelected));
            this.RaisePropertyChanged(nameof(ShowCapacity));
            this.RaisePropertyChanged(nameof(ShowDescription));
            CapacityError = IsChatSelected ? null : SessionRules.ValidateCapacity(Capacity);
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
        int? categoryId = null, bool privateMode = false, ChatSessionModel? editingGroup = null)
    {
        _coordinatorSession = coordinatorSession;
        Strings.LanguageChanged += OnLanguageChanged;
        _categoryId = categoryId;
        IsPrivateMode = privateMode;
        _editingGroup = editingGroup;
        IsGroup = editingGroup != null;
        if (editingGroup != null) Name = editingGroup.Name;
        SelectDirectCommand = ReactiveCommand.Create(() => { IsGroup = false; });
        SelectGroupCommand = ReactiveCommand.Create(() => { IsGroup = true; });
        ToggleMemberCommand = ReactiveCommand.Create<ChatMemberChoiceVM>(ToggleMember);

        SelectedType = initialType == SessionType.Chat ? ChatOption : HybridOption;
        SelectedType.IsSelected = true;
        Capacity = HybridOption.DefaultCapacity;

        var isValid = this.WatchProperties(nameof(Name), nameof(Capacity), nameof(Description), nameof(SelectedType),
                nameof(IsGroup), nameof(SelectedCountText), nameof(SelectedOwner))
            .Select(vm => vm.IsPrivateMode
                ? vm.ValidPrivateMembers && (!vm.IsGroup || SessionRules.ValidateName(vm.Name) == null)
                : SessionRules.ValidateName(vm.Name) == null
                    && (vm.IsChatSelected || SessionRules.ValidateCapacity(vm.Capacity) == null)
                    && (vm.SelectedType.Type != SessionType.Chat || ValidateDescription(vm.Description) == null));

        CreateCommand = ReactiveCommand.CreateFromTask(CreateAsync, isValid);
        SelectTypeCommand = ReactiveCommand.Create<SessionTypeOptionVM>(SelectType, CreateCommand.IsExecuting.Select(busy => !busy));
        CloseCommand = ReactiveCommand.CreateFromTask(async () => { await CloseWindow.Handle(RxVoid.Default); },
            CreateCommand.IsExecuting.Select(busy => !busy));

        _disposables.Add(SelectDirectCommand);
        _disposables.Add(SelectGroupCommand);
        _isBusy = CreateCommand.IsExecuting.ToProperty(this, nameof(IsBusy));

        // The name has no error until it is first edited, an empty untouched form is not a mistake
        _disposables.Add(this.WatchProperty(nameof(SearchQuery), vm => vm.SearchQuery)
            .Skip(1).Throttle(TimeSpan.FromMilliseconds(250))
            .Subscribe(query => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = SearchMembersAsync(query))));
        _disposables.Add(this.WatchProperty(nameof(Name), vm => vm.Name).Skip(1).Subscribe(name =>
        {
            NameError = IsPrivateMode && !IsGroup ? null : SessionRules.ValidateName(name);
            GeneralError = null;
        }));
        _disposables.Add(this.WatchProperty(nameof(Capacity), vm => vm.Capacity)
            .Subscribe(capacity => CapacityError = IsChatSelected ? null : SessionRules.ValidateCapacity(capacity)));
        _disposables.Add(this.WatchProperty(nameof(Description), vm => vm.Description)
            .Subscribe(description => DescriptionError = ValidateDescription(description)));
    }

    private void OnLanguageChanged()
    {
        this.RaisePropertyChanged(nameof(DialogTitle));
        this.RaisePropertyChanged(nameof(SubmitText));
        this.RaisePropertyChanged(nameof(BusyText));
        this.RaisePropertyChanged(nameof(SelectedCountText));
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
    }

    private async Task CreateAsync()
    {
        if (IsPrivateMode)
        {
            if (!ValidPrivateMembers) return;
            var name = IsGroup ? Name.Trim() : "Direct";
            var members = SelectedMembers.Select(member => member.Id).ToArray();
            var privateResult = IsEditingGroup
                ? await _coordinatorSession.ChangeGroup(_editingGroup!.Id, name, members, SelectedOwner!.Id)
                : await CreatePrivateAsync(name, members);
            if (privateResult.IsSuccess) await CloseWindow.Handle(RxVoid.Default);
            else GeneralError = Strings.Format("createSession.notCreated", ("reason", privateResult));
            return;
        }
        NameError = SessionRules.ValidateName(Name);
        GeneralError = null;

        CapacityError = IsChatSelected ? null : SessionRules.ValidateCapacity(Capacity);
        DescriptionError = IsChatSelected ? ValidateDescription(Description) : null;
        if (NameError != null || CapacityError != null || DescriptionError != null)
            return;

        NewSessionOptions options = SelectedType.Type switch
        {
            SessionType.Chat => new NewChatSessionOptions(
                Description: string.IsNullOrWhiteSpace(Description) ? null : Description.Trim()),
            _ => new NewHybridSessionOptions(WebRTCEngine.GStreamer)
        };

        var session = new NewSessionModel(Name.Trim(), _coordinatorSession.GetCurrentUser().Id,
            IsChatSelected ? Limits.SessionCapacityUnlimited : (int)Capacity!.Value, options, _categoryId);

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

    private async Task<Client.Core.Coordinator.RequestResult> CreatePrivateAsync(string name, int[] members)
    {
        var result = await _coordinatorSession.CreateConversation(new NewSessionModel(name,
            _coordinatorSession.GetCurrentUser().Id, Limits.SessionCapacityUnlimited,
            new NewChatSessionOptions(Kind: IsGroup ? ChatKind.Group : ChatKind.Direct, MemberIds: members)));
        if (result.IsSuccess) CreatedSession = result.Value;
        return result.Result;
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
        _disposed = true;
        Strings.LanguageChanged -= OnLanguageChanged;
        _searchVersion++;
        ToggleMemberCommand.Dispose();
        _disposables.Dispose();
        _isBusy.Dispose();
        SelectTypeCommand.Dispose();
        CreateCommand.Dispose();
        CloseCommand.Dispose();
    }
}
