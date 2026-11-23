using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Chat;
using Client.Application.Coordinator;
using Client.Application.Coordinator.Users;
using Client.Application.Sessions.Hybrid;
using Client.Avalonia.Localization;
using Client.Avalonia.Styles.Animation;
using Client.Core.Coordinator;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;
using Client.Services;
using Client.ViewModels.Dialogs;
using Client.ViewModels.Main;
using Client.ViewModels.Sessions.Hybrid;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using Shared.Enums;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.List;

public partial class SessionsListVM : ReactiveObject
{
    public Interaction<MessageBoxVM, Result> ShowMessageBox { get; }

    public SessionsListState State
    {
        get;
        private set
        {
            if (value != SessionsListState.Reconnecting)
                _isListKept = false;

            this.RaiseAndSetIfChanged(ref field, value);
            RaiseStateProperties();
        }
    } = SessionsListState.Offline;

    // Set while the connection is being restored with a list to show meanwhile
    private bool _isListKept;

    public bool IsOffline => State == SessionsListState.Offline;
    /// <summary>
    /// The connection is being restored and there was no list to keep on screen meanwhile.
    /// </summary>
    public bool IsReconnecting => State == SessionsListState.Reconnecting && !_isListKept;

    /// <summary>
    /// The list is the one from before the connection was lost: shown, but not to be acted on
    /// until the connection is back and it is loaded again.
    /// </summary>
    public bool IsStale => State == SessionsListState.Reconnecting && _isListKept;
    public bool IsLoading => State == SessionsListState.Loading;
    public bool IsFailed => State == SessionsListState.Failed;
    // An empty category is something to show too
    public bool IsEmpty => State == SessionsListState.Loaded && Rows.Count == 0;
    public bool HasSessions => IsStale || (State == SessionsListState.Loaded && Rows.Count != 0);

    /// <summary>
    /// Sessions and categories can be created from the list: it is loaded and connected.
    /// </summary>
    public bool CanCreate => State == SessionsListState.Loaded;
    public bool HasPrivateChats => (IsStale || State == SessionsListState.Loaded) && PrivateChats.Count != 0;
    public bool IsPrivateChatsEmpty => State == SessionsListState.Loaded && PrivateChats.Count == 0;
    public bool CanCreateSession => IsPrivateMode || Access.CanCreateSessions;
    public bool CanCreateCategory => !IsPrivateMode && Access.CanManageSessions;
    public bool CanShowChannels => IsPrivateMode && Access.CanViewServerChannels;
    public bool IsPrivateMode
    {
        get;
        set
        {
            value = value || !Access.CanViewServerChannels;
            if (field == value) return;
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(CanShowChannels));
            this.RaisePropertyChanged(nameof(CanCreateSession));
            this.RaisePropertyChanged(nameof(CanCreateCategory));
            RebuildRows();
            RaiseStateProperties();
            if (CoordinatorInstanceId is { } id) _ = _preferences.SavePrivateModeAsync(id, value);
        }
    }
    public void ToggleListMode() => IsPrivateMode = !IsPrivateMode;

    /// <summary>What the current user may do; the list offers only that.</summary>
    public AccessVM Access { get; }

    public ReactiveCommand<RxVoid, RxVoid> ConnectCommand { get; }

    public string? ErrorMessage
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The item the list control holds as selected. It only carries the clicks: the highlight
    /// follows <see cref="ISessionVM.IsShown"/>, so it never marks a session that is not on screen.
    /// </summary>
    public ISessionListRow? SelectedSession
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);

            // Moving rows around makes the list control pick another item now and then
            if (_isRebuildingRows)
                return;

            // A chat opens on a click. A hybrid session is joined by a click on its header, which
            // the view handles, so the selection only brings back the one we are already in; the
            // keyboard moving through the list must not join sessions.
            if (value is ChatSessionVM or HybridSessionVM { IsEntered: true } && !((ISessionVM)value).IsShown)
                _showSessionCommand.Execute(((ISessionVM)value).Model).Subscribe();

            // A category header is not something to show; the selection goes back to what is shown
            if (value is SessionCategoryVM)
                Dispatcher.UIThread.Post(RestoreSelection);
        }
    }

    /// <summary>
    /// The sessions of the coordinator, in no particular order. <see cref="Rows"/> is what the list shows.
    /// </summary>
    public ObservableCollection<ISessionVM> Sessions { get; } = new();

    /// <summary>
    /// The rows of the list in the order of the layout: sessions and category headers.
    /// </summary>
    public ObservableCollection<ISessionListRow> Rows { get; } = new();

    /// <summary>Personal conversations in recent-message order, independent of the sidebar mode.</summary>
    public IReadOnlyList<ChatSessionVM> PrivateChats
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = [];

    // The hybrid session we are in, marked in the list; kept so that items added later are marked too
    private int? _enteredHybridSessionId;
    private HybridSessionStatusBarVM? _enteredHybridSessionControls;

    // The session the main window shows, kept for the same reason
    private (int Id, bool IsHybrid)? _shownSession;

    private readonly ReactiveCommand<SessionModel, RxVoid> _showSessionCommand;
    private readonly ReactiveCommand<int, RxVoid> _leaveChatCommand;
    private readonly ReactiveCommand<int, RxVoid> _leaveHybridSessionCommand;
    private readonly ReactiveCommand<SessionModel, RxVoid> _editSessionCommand;
    private readonly ReactiveCommand<SessionModel, RxVoid> _editPermissionsCommand;
    private readonly ReactiveCommand<int, RxVoid> _deleteSessionCommand;
    private readonly ReactiveCommand<int, RxVoid> _viewProfileCommand;
    private readonly ReactiveCommand<(int SessionId, int UserId), RxVoid> _kickPeerCommand;
    private readonly ReactiveCommand<(int UserId, string Name), RxVoid> _banUserCommand;
    private readonly ReactiveCommand<(int SessionId, int UserId, bool IsApproved), RxVoid> _admitPeerCommand;
    private readonly ReactiveCommand<(int SessionId, int UserId, string? Message), RxVoid> _pokePeerCommand;
    private readonly ReactiveCommand<int, RxVoid> _toggleChatMuteCommand;

    private ICoordinatorSession? _coordinatorSession;
    private readonly EventSubscriptions _coordinatorSubscriptions = new();

    private string? CoordinatorInstanceId => _coordinatorSession?.GetCurrentUser().CoordinatorInstanceId;

    // Counts attaches and detaches: a load outlived by a detach drops what it got
    private int _attachId;

    private readonly IPeerPlaybackService _playbackService;
    private readonly IVoiceActivityService _voiceActivity;
    private readonly IScreenWatchService _screenWatch;
    private readonly PeerStatsService _peerStats;
    private readonly SessionListPreferences _preferences;
    private readonly ChatReadTracker _readTracker;
    private readonly ILogger<SessionsListVM> _logger;

    public SessionsListVM(ReactiveCommand<RxVoid, RxVoid> connectCommand,
        ReactiveCommand<SessionModel, RxVoid> showSessionCommand,
        ReactiveCommand<int, RxVoid> leaveChatCommand,
        ReactiveCommand<int, RxVoid> leaveHybridSessionCommand,
        ReactiveCommand<SessionModel, RxVoid> editSessionCommand,
        ReactiveCommand<SessionModel, RxVoid> editPermissionsCommand,
        ReactiveCommand<int, RxVoid> viewProfileCommand,
        ReactiveCommand<(int SessionId, int UserId), RxVoid> kickPeerCommand,
        ReactiveCommand<(int UserId, string Name), RxVoid> banUserCommand,
        ReactiveCommand<(int SessionId, int UserId, bool IsApproved), RxVoid> admitPeerCommand,
        ReactiveCommand<(int SessionId, int UserId, string? Message), RxVoid> pokePeerCommand,
        ReactiveCommand<int?, RxVoid> createSessionCommand,
        IPeerPlaybackService playbackService, IVoiceActivityService voiceActivity, IScreenWatchService screenWatch,
        PeerStatsService peerStats,
        SessionListPreferences preferences,
        ChatReadTracker readTracker,
        AccessVM access,
        ILogger<SessionsListVM> logger)
    {
        Access = access;
        Access.PermissionsUpdated += _ => ApplyAccess();
        ConnectCommand = connectCommand;
        _showSessionCommand = showSessionCommand;
        _leaveChatCommand = leaveChatCommand;
        _leaveHybridSessionCommand = leaveHybridSessionCommand;
        _editSessionCommand = editSessionCommand;
        _editPermissionsCommand = editPermissionsCommand;
        _viewProfileCommand = viewProfileCommand;
        _kickPeerCommand = kickPeerCommand;
        _banUserCommand = banUserCommand;
        _admitPeerCommand = admitPeerCommand;
        _pokePeerCommand = pokePeerCommand;
        _createSessionCommand = createSessionCommand;
        _playbackService = playbackService;
        _preferences = preferences;
        _readTracker = readTracker;
        _readTracker.StateChanged += OnReadStateChanged;
        _logger = logger;

        _playbackService.Changed += OnPlaybackChanged;
        _voiceActivity = voiceActivity;
        _voiceActivity.Changed += OnSpeakingChanged;
        _screenWatch = screenWatch;
        _screenWatch.Changed += OnScreenWatchChanged;
        _peerStats = peerStats;
        _deleteSessionCommand = ReactiveCommand.CreateFromTask<int>(DeleteSession);
        _toggleChatMuteCommand = ReactiveCommand.CreateFromTask<int>(ToggleChatMute);

        ShowMessageBox = new Interaction<MessageBoxVM, Result>();
        CreateCategoryCommand = ReactiveCommand.CreateFromTask(CreateCategory);
        CreateSessionCommand = ReactiveCommand.Create(() => { _createSessionCommand.Execute(null).Subscribe(); });
        MoveTargets.Add(new MoveTargetVM(null, Strings.Get("sessions.category.none"), MoveToCategory));

        Rows.CollectionChanged += (_, _) =>
        {
            this.RaisePropertyChanged(nameof(IsEmpty));
            this.RaisePropertyChanged(nameof(HasSessions));
        };
        if (!Access.CanViewServerChannels)
            IsPrivateMode = true;
    }

    private void RaiseStateProperties()
    {
        this.RaisePropertyChanged(nameof(IsOffline));
        this.RaisePropertyChanged(nameof(IsReconnecting));
        this.RaisePropertyChanged(nameof(IsStale));
        this.RaisePropertyChanged(nameof(IsLoading));
        this.RaisePropertyChanged(nameof(IsFailed));
        this.RaisePropertyChanged(nameof(IsEmpty));
        this.RaisePropertyChanged(nameof(HasSessions));
        this.RaisePropertyChanged(nameof(CanCreate));
        this.RaisePropertyChanged(nameof(HasPrivateChats));
        this.RaisePropertyChanged(nameof(IsPrivateChatsEmpty));
    }

    /// <summary>
    /// Shown while the sessions are being retrieved, before the coordinator is attached.
    /// </summary>
    public void ShowLoading()
    {
        // The list from before the loss stays until the new one replaces it
        if (!IsStale)
            State = SessionsListState.Loading;
    }

    /// <summary>
    /// Detaches from a coordinator session that restores a lost connection on its own. The list
    /// stays on screen as it was, see <see cref="IsStale"/>, until the session is attached again.
    /// </summary>
    public void ShowReconnecting()
    {
        Unsubscribe();
        ErrorMessage = null;

        _isListKept = State == SessionsListState.Loaded && (Rows.Count != 0 || PrivateChats.Count != 0) || IsStale;
        if (!_isListKept)
            ClearList();

        State = SessionsListState.Reconnecting;
    }

    public async Task AttachCoordinator(ICoordinatorSession coordinatorSession)
    {
        if (!IsStale)
            State = SessionsListState.Loading;

        // An attach still loading for an earlier connection gives way to this one
        Unsubscribe();
        var attachId = _attachId;
        var session = _coordinatorSession = coordinatorSession;
        await _readTracker.AttachAsync(session.GetCurrentUser().CoordinatorInstanceId, session.GetCurrentUser().Id);
        if (attachId != _attachId)
            return;
        _coordinatorSubscriptions
            // Before the list, so that no message comes between the counts and the subscription
            .Add(() => session.TextMessageAdded += _readTracker.OnMessageAdded,
                () => session.TextMessageAdded -= _readTracker.OnMessageAdded)
            // Before the list as well: a layout newer than the one the list comes with replaces it
            .Add(() => session.SessionLayoutChanged += OnSessionLayoutChanged,
                () => session.SessionLayoutChanged -= OnSessionLayoutChanged);
        await LoadSessionList(session, attachId);

        // Detached while loading: the connection is gone and the handlers were taken off already
        if (attachId != _attachId)
            return;

        _coordinatorSubscriptions
            .Add(() => session.HybridSessionAdded += OnHybridSessionAdded, () => session.HybridSessionAdded -= OnHybridSessionAdded)
            .Add(() => session.HybridSessionDeleted += RemoveSession, () => session.HybridSessionDeleted -= RemoveSession)
            .Add(() => session.HybridSessionReconfigured += OnHybridSessionReconfigured,
                () => session.HybridSessionReconfigured -= OnHybridSessionReconfigured)
            .Add(() => session.ChatSessionAdded += AddNewChatSession, () => session.ChatSessionAdded -= AddNewChatSession)
            .Add(() => session.ChatSessionDeleted += RemoveChatSession, () => session.ChatSessionDeleted -= RemoveChatSession)
            .Add(() => session.ChatSessionReconfigured += OnChatSessionReconfigured,
                () => session.ChatSessionReconfigured -= OnChatSessionReconfigured)
            .Add(() => session.ChatCallChanged += OnChatCallChanged, () => session.ChatCallChanged -= OnChatCallChanged)
            .Add(() => session.HybridSessionPeerJoined += AddPeerToSession, () => session.HybridSessionPeerJoined -= AddPeerToSession)
            .Add(() => session.HybridSessionPeerLeft += RemovePeerFromSession,
                () => session.HybridSessionPeerLeft -= RemovePeerFromSession)
            .Add(() => session.HybridSessionPeerStateChanged += UpdatePeerStatus,
                () => session.HybridSessionPeerStateChanged -= UpdatePeerStatus)
            .Add(() => session.HybridSessionPeerMicrophoneMuteChanged += UpdatePeerMicrophone,
                () => session.HybridSessionPeerMicrophoneMuteChanged -= UpdatePeerMicrophone)
            .Add(() => session.HybridSessionPeerSoundMuteChanged += UpdatePeerSound,
                () => session.HybridSessionPeerSoundMuteChanged -= UpdatePeerSound)
            .Add(() => session.HybridSessionPeerAfkChanged += UpdatePeerAfk,
                () => session.HybridSessionPeerAfkChanged -= UpdatePeerAfk)
            .Add(() => session.HybridSessionUpdated += OnHybridSessionUpdated,
                () => session.HybridSessionUpdated -= OnHybridSessionUpdated)
            .Add(() => session.UserUpdated += OnUserUpdated, () => session.UserUpdated -= OnUserUpdated);

        await LoadReadStates(session, attachId);
    }

    public void DetachCoordinator()
    {
        State = SessionsListState.Offline;
        ErrorMessage = null;

        ClearList();
        Unsubscribe();
        _readTracker.Detach();
    }

    private void Unsubscribe()
    {
        _coordinatorSubscriptions.Clear();
        _coordinatorSession = null;
        _attachId++;
    }

    private async Task LoadSessionList(ICoordinatorSession coordinatorSession, int attachId)
    {
        // A list kept through a reconnect is replaced only once the new one has come
        if (!IsStale)
            ClearList();

        if (coordinatorSession.ConnectionStatus == CoordinatorState.Connected)
        {
            try
            {
                var result = await coordinatorSession.GetAllSessions();
                if (attachId != _attachId)
                    return;

                if (!result.Result.IsSuccess)
                {
                    ShowFailed(result.Result.IsRejected
                        ? Strings.Get("sessions.fetch.rateLimited")
                        : Strings.Get("sessions.fetch.serverError"));
                    return;
                }

                var coordinatorInstanceId = coordinatorSession.GetCurrentUser().CoordinatorInstanceId;
                var mutedChatIds = await _preferences.LoadMutedChatsAsync(coordinatorInstanceId);
                var collapsedCategoryIds = await _preferences.LoadCollapsedCategoriesAsync(coordinatorInstanceId);
                var privateMode = await _preferences.LoadPrivateModeAsync(coordinatorInstanceId);
                if (attachId != _attachId) return;
                IsPrivateMode = privateMode;

                ClearList();
                SetLayout(result.Value.Layout, collapsedCategoryIds);

                foreach (var sessionModel in result.Value.Sessions.Values)
                {
                    if (attachId != _attachId)
                        return;

                    if (sessionModel.SessionType == SessionType.Hybrid
                        && sessionModel is HybridSessionModel hybridSessionModel)
                        await AddNewHybridSession(hybridSessionModel);
                    else if (sessionModel.SessionType == SessionType.Chat
                             && sessionModel is ChatSessionModel chatSessionModel)
                        AddChatSession(chatSessionModel, mutedChatIds.Contains(chatSessionModel.Id));
                }

                // Chats deleted while we were away
                foreach (var chatId in mutedChatIds)
                {
                    if (!Sessions.Any(session => session is ChatSessionVM && session.Model.Id == chatId))
                        _ = _preferences.SaveChatMuteAsync(coordinatorInstanceId, chatId, false);
                }

                // Categories deleted while we were away
                foreach (var categoryId in collapsedCategoryIds)
                {
                    if (_serverLayout.FindCategory(categoryId) == null)
                        _ = _preferences.SaveCategoryCollapseAsync(coordinatorInstanceId, categoryId, false);
                }

                State = SessionsListState.Loaded;
            }
            catch (TimeoutException) when (attachId == _attachId)
            {
                ShowFailed(Strings.Get("sessions.fetch.timedOut"));
                throw;
            }
        }
        else
        {
            ShowFailed(Strings.Get("sessions.fetch.connectionFailed"));
        }
    }

    private void ShowFailed(string message)
    {
        ErrorMessage = message;
        State = SessionsListState.Failed;
    }

    private async Task AddNewHybridSession(HybridSessionModel hybridSessionModel)
    {
        var session = AddHybridSessionVM(hybridSessionModel);
        var result = await LoadPeersAsync(_coordinatorSession!, session);
        if (!result.IsSuccess)
        {
            LogPeersRequestFailed(hybridSessionModel.Id, hybridSessionModel.Name, result);
            session.IsCorrupted = true;
        }
    }

    // A session created just now has no peers yet, so there is nothing to load for it
    private void OnHybridSessionAdded(HybridSessionModel session) => AddHybridSessionVM(session);

    /// <summary>
    /// Marks the hybrid session we are in, or none. The list outlines it.
    /// </summary>
    public void SetEnteredHybridSession(int? sessionId, HybridSessionStatusBarVM? controls = null)
    {
        _enteredHybridSessionId = sessionId;
        _enteredHybridSessionControls = controls;

        foreach (var session in HybridSessions)
            MarkEntered(session);
        // A call runs in a session under the ID of its chat
        foreach (var chat in Sessions.OfType<ChatSessionVM>())
            chat.IsEntered = CallSession != null && CallSession == chat.CallPeers && chat.Model.Id == sessionId;

        UpdateFolding();
    }

    private void MarkEntered(HybridSessionVM session)
    {
        session.IsEntered = session.Model.Id == _enteredHybridSessionId;
        session.SetSessionControls(session.IsEntered ? _enteredHybridSessionControls : null);
    }

    /// <summary>
    /// Marks the session the main window shows, or none, and moves the selection of the list to it.
    /// </summary>
    public void SetShownSession(int? sessionId, bool isHybrid)
    {
        _shownSession = sessionId is { } id ? (id, isHybrid) : null;

        foreach (var session in Sessions)
            MarkShown(session);

        UpdateFolding();
        RestoreSelection();
    }

    private void RestoreSelection() => SelectedSession = Sessions.FirstOrDefault(session => session.IsShown);

    private void MarkShown(ISessionVM session) =>
        session.IsShown = _shownSession is { } shown &&
                          shown.Id == session.Model.Id && shown.IsHybrid == session is HybridSessionVM;

    private HybridSessionVM CreateHybridSessionVM(HybridSessionModel sessionModel)
    {
        var model = new HybridSessionVM(sessionModel, _coordinatorSession!.GetCurrentUser().Id, _playbackService,
            _screenWatch, _peerStats, _showSessionCommand, _leaveHybridSessionCommand, _editSessionCommand,
            _editPermissionsCommand, _deleteSessionCommand, _viewProfileCommand, _kickPeerCommand,
            _banUserCommand, _admitPeerCommand, _pokePeerCommand);
        model.ApplyAccess(Access.Permissions);
        MarkEntered(model);
        MarkShown(model);
        model.WatchProperty(nameof(HybridSessionVM.SelectedPeer), x => x.SelectedPeer)
            .Where(peer => peer != null)
            .Subscribe(_ => ClearPeerSelectionExcept(model));
        return model;
    }

    private HybridSessionVM AddHybridSessionVM(HybridSessionModel sessionModel)
    {
        var model = CreateHybridSessionVM(sessionModel);
        model.MoveTargets = MoveTargets;
        Sessions.Add(model);
        RebuildRows();
        return model;
    }

    // Raised on the socket thread
    private void OnHybridSessionReconfigured(HybridSessionModel model)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // A call session follows its call, not the server's model of it
            var session = Sessions.OfType<HybridSessionVM>().FirstOrDefault(s => s.Model.Id == model.Id && !s.IsLeaving);

            if (session == null)
                return;

            session.Model = model;
            // The host may have changed
            session.ApplyAccess(Access.Permissions);
        });
    }

    // Raised on the socket thread
    private void RemoveSession(int sessionId)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            var session = Sessions.FirstOrDefault(s => s.Model.Id == sessionId && !s.IsLeaving);

            if (session == null)
                return;

            // The item folds away first, so the sessions below slide up instead of jumping
            session.IsLeaving = true;
            await Task.Delay(Presence.LeaveTime);

            Sessions.Remove(session);
            if (session is ChatSessionVM chat) chat.Dispose();
            RebuildRows();
        });
    }

    /// <summary>The voice channels of the list and the calls running in its chats.</summary>
    private IEnumerable<HybridSessionVM> HybridSessions => Sessions.OfType<HybridSessionVM>()
        .Concat(Sessions.OfType<ChatSessionVM>().Select(chat => chat.CallPeers).OfType<HybridSessionVM>());

    /// <summary>A chat of the list by its id.</summary>
    public ChatSessionModel? FindChat(int chatId) => FindSession<ChatSessionVM>(chatId)?.Model as ChatSessionModel;

    /// <summary>A voice channel of the list, or the call of a chat: a call runs under the ID of its chat.</summary>
    public HybridSessionVM? FindHybridSession(int sessionId) =>
        FindSession<HybridSessionVM>(sessionId) ?? FindSession<ChatSessionVM>(sessionId)?.CallPeers;

    /// <summary>
    /// The sessions of the list by their id, of one kind.
    /// </summary>
    private T? FindSession<T>(int sessionId) where T : class, ISessionVM =>
        Sessions.OfType<T>().FirstOrDefault(session => session.Model.Id == sessionId);

    /// <summary>Shows again what the current user may do, after their roles changed.</summary>
    private void ApplyAccess()
    {
        if (!Access.CanViewServerChannels)
            IsPrivateMode = true;
        this.RaisePropertyChanged(nameof(CanShowChannels));
        foreach (var chat in Sessions.OfType<ChatSessionVM>()) chat.CallPeers?.ApplyAccess(Access.Permissions);
        this.RaisePropertyChanged(nameof(CanCreateSession));
        this.RaisePropertyChanged(nameof(CanCreateCategory));
        foreach (var session in Sessions)
        {
            switch (session)
            {
                case HybridSessionVM hybrid:
                    hybrid.ApplyAccess(Access.Permissions);
                    break;
                case ChatSessionVM chat:
                    chat.ApplyAccess(Access.Permissions);
                    break;
            }
        }

        foreach (var category in _categories.Values)
        {
            category.CanManage = Access.CanManageSessions;
            category.CanCreateSessions = Access.CanCreateSessions;
        }
    }

    private async Task DeleteSession(int sessionId)
    {
        if (await ShowMessageBox.Handle(new MessageBoxVM(Icon.Question, Buttons.YesNo,
                Strings.Get("sessions.delete.question"),
                Strings.Get("sessions.delete.title"))) == Result.Yes)
        {
            var result = await _coordinatorSession!.DeleteSession(sessionId);
            // A rejection is reported by the main window
            if (!(result.IsSuccess || result.IsRejected))
            {
                await ShowMessageBox.Handle(new MessageBoxVM(Icon.Fatal, Buttons.Ok,
                    Strings.Format("sessions.delete.failed", ("session", sessionId), ("code", (int?)result.Error)),
                    Strings.Get("common.error")));
            }
        }
    }
}
