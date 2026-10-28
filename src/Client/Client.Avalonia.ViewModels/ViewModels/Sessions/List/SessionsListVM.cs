using Client.Avalonia.Localization;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using Shared.Enums;
using Shared.Models.Media;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Core.Events;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;
using Client.Core.Users;
using Client.Application.Chat;
using Client.Application.Coordinator;
using Client.Application.Coordinator.Users;
using Client.Application.Sessions.Hybrid;
using Client.Avalonia.Styles.Animation;
using Client.Services;
using Client.ViewModels.Main;
using Client.ViewModels.Dialogs;
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

    // The hybrid session we are in, marked in the list; kept so that items added later are marked too
    private int? _enteredHybridSessionId;

    // The session the main window shows, kept for the same reason
    private (int Id, bool IsHybrid)? _shownSession;

    private readonly ReactiveCommand<SessionModel, RxVoid> _showSessionCommand;
    private readonly ReactiveCommand<int, RxVoid> _leaveHybridSessionCommand;
    private readonly ReactiveCommand<SessionModel, RxVoid> _editHybridSessionCommand;
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
    private readonly SessionListPreferences _preferences;
    private readonly ChatReadTracker _readTracker;
    private readonly ILogger<SessionsListVM> _logger;

    public SessionsListVM(ReactiveCommand<RxVoid, RxVoid> connectCommand,
        ReactiveCommand<SessionModel, RxVoid> showSessionCommand,
        ReactiveCommand<int, RxVoid> leaveHybridSessionCommand,
        ReactiveCommand<SessionModel, RxVoid> editHybridSessionCommand,
        ReactiveCommand<SessionModel, RxVoid> editPermissionsCommand,
        ReactiveCommand<int, RxVoid> viewProfileCommand,
        ReactiveCommand<(int SessionId, int UserId), RxVoid> kickPeerCommand,
        ReactiveCommand<(int UserId, string Name), RxVoid> banUserCommand,
        ReactiveCommand<(int SessionId, int UserId, bool IsApproved), RxVoid> admitPeerCommand,
        ReactiveCommand<(int SessionId, int UserId, string? Message), RxVoid> pokePeerCommand,
        ReactiveCommand<int?, RxVoid> createSessionCommand,
        IPeerPlaybackService playbackService, IVoiceActivityService voiceActivity, IScreenWatchService screenWatch,
        SessionListPreferences preferences,
        ChatReadTracker readTracker,
        AccessVM access,
        ILogger<SessionsListVM> logger)
    {
        Access = access;
        Access.PermissionsUpdated += _ => ApplyAccess();
        ConnectCommand = connectCommand;
        _showSessionCommand = showSessionCommand;
        _leaveHybridSessionCommand = leaveHybridSessionCommand;
        _editHybridSessionCommand = editHybridSessionCommand;
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
        _deleteSessionCommand = ReactiveCommand
            .CreateFromTask<int>(DeleteSession);
        _toggleChatMuteCommand = ReactiveCommand
            .CreateFromTask<int>(ToggleChatMute);
        
        ShowMessageBox = new Interaction<MessageBoxVM, Result>();
        CreateCategoryCommand = ReactiveCommand.CreateFromTask(CreateCategory);
        CreateSessionCommand = ReactiveCommand.Create(() => { _createSessionCommand.Execute(null).Subscribe(); });
        MoveTargets.Add(new MoveTargetVM(null, Strings.Get("sessions.category.none"), MoveToCategory));
        
        Rows.CollectionChanged += (_, _) =>
        {
            this.RaisePropertyChanged(nameof(IsEmpty));
            this.RaisePropertyChanged(nameof(HasSessions));
        };
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

        _isListKept = State == SessionsListState.Loaded && Rows.Count != 0 || IsStale;
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
        _readTracker.Attach(session.GetCurrentUser().Id, (chatId, messageId) => session.MarkChatRead(chatId, messageId));
        _coordinatorSubscriptions
            // Before the list, so that no message comes between the counts and the subscription
            .Add(() => session.TextMessageAdded += _readTracker.OnMessageAdded,
                () => session.TextMessageAdded -= _readTracker.OnMessageAdded)
            .Add(() => session.ChatReadStateChanged += _readTracker.OnServerState,
                () => session.ChatReadStateChanged -= _readTracker.OnServerState)
            // Before the list as well: a layout newer than the one the list comes with replaces it
            .Add(() => session.SessionLayoutChanged += OnSessionLayoutChanged,
                () => session.SessionLayoutChanged -= OnSessionLayoutChanged);
        await LoadSessionList(session, attachId);
        await LoadReadStates(session, attachId);

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
                if (attachId != _attachId)
                    return;

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

    private async Task LoadReadStates(ICoordinatorSession coordinatorSession, int attachId)
    {
        if (attachId != _attachId || State != SessionsListState.Loaded)
            return;

        var (result, states) = await coordinatorSession.RequestChatReadStates();
        if (attachId != _attachId)
            return;

        if (result.IsSuccess && states != null)
            _readTracker.Load(states);
        else
            LogReadStatesLoadFailed(result);
    }

    // Raised on the socket thread or the UI thread
    private void OnReadStateChanged(ChatReadState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (FindSession<ChatSessionVM>(state.ChatId) is { } chat)
            {
                chat.ApplyReadState(state);
                RefreshCategoryCounts();
            }
        });
    }

    private void ShowFailed(string message)
    {
        ErrorMessage = message;
        State = SessionsListState.Failed;
    }
    
    private async Task AddNewHybridSession(HybridSessionModel hybridSessionModel)
    {
        var model = AddHybridSessionVM(hybridSessionModel);

        var result = await _coordinatorSession!.GetAllHybridSessionPeers(hybridSessionModel.Id);
        if (!result.Result.IsSuccess)
        {
            LogPeersRequestFailed(hybridSessionModel.Id, hybridSessionModel.Name, result.Result);
            model.IsCorrupted = true;
            return;
        }

        var peers = result.Value!.Values;

        foreach (var peer in peers)
            model.AddPeerToSession(peer);

        // The peers show the defaults until then and get the stored settings through Changed
        _ = _playbackService.LoadAsync(CoordinatorInstanceId!, peers.Select(peer => peer.UserId).ToList());

        var users = await _coordinatorSession.GetUsers(peers.Select(peer => peer.UserId).ToList());

        foreach (var (userId, user) in users)
            model.UpdateUserModel(userId, user);
    }

    // A chat created just now is not muted
    private void AddNewChatSession(ChatSessionModel chatSessionModel) => AddChatSession(chatSessionModel, false);

    private void AddChatSession(ChatSessionModel chatSessionModel, bool isMuted)
    {
        var model = new ChatSessionVM(
            chatSessionModel, 
            _showSessionCommand, 
            _deleteSessionCommand,
            _toggleChatMuteCommand,
            _editPermissionsCommand)
        {
            IsMuted = isMuted
        };
        if (_readTracker.Get(chatSessionModel.Id) is { } readState)
            model.ApplyReadState(readState);
        model.ApplyAccess(Access.Permissions);
        
        MarkShown(model);
        model.MoveTargets = MoveTargets;
        Sessions.Add(model);
        RebuildRows();
    }

    private async Task ToggleChatMute(int chatId)
    {
        if (CoordinatorInstanceId is not { } coordinatorInstanceId || FindSession<ChatSessionVM>(chatId) is not { } chat)
            return;

        chat.IsMuted = !chat.IsMuted;
        RefreshCategoryCounts();
        await _preferences.SaveChatMuteAsync(coordinatorInstanceId, chatId, chat.IsMuted);
    }

    // A session created just now has no peers yet, so there is nothing to load for it
    private void OnHybridSessionAdded(HybridSessionModel session) => AddHybridSessionVM(session);

    /// <summary>
    /// Marks the hybrid session we are in, or none. The list outlines it.
    /// </summary>
    public void SetEnteredHybridSession(int? sessionId)
    {
        _enteredHybridSessionId = sessionId;

        foreach (var session in Sessions.OfType<HybridSessionVM>())
            MarkEntered(session);

        UpdateFolding();
    }

    private void MarkEntered(HybridSessionVM session) =>
        session.IsEntered = session.Model.Id == _enteredHybridSessionId;

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

    private HybridSessionVM AddHybridSessionVM(HybridSessionModel sessionModel)
    {
        var model = new HybridSessionVM(sessionModel, _coordinatorSession!.GetCurrentUser().Id, _playbackService,
            _screenWatch, _showSessionCommand, _leaveHybridSessionCommand, _editHybridSessionCommand,
            _editPermissionsCommand, _deleteSessionCommand, _viewProfileCommand, _kickPeerCommand,
            _banUserCommand, _admitPeerCommand, _pokePeerCommand);
        model.ApplyAccess(Access.Permissions);
        MarkEntered(model);
        MarkShown(model);

        // Only one peer can be selected across all hybrid sessions
        model.WatchProperty(nameof(HybridSessionVM.SelectedPeer), x => x.SelectedPeer)
            .Where(peer => peer != null)
            .Subscribe(_ => ClearPeerSelectionExcept(model));

        model.MoveTargets = MoveTargets;
        Sessions.Add(model);
        RebuildRows();
        return model;
    }

    private void ClearPeerSelectionExcept(HybridSessionVM selectedIn)
    {
        foreach (var session in Sessions.OfType<HybridSessionVM>())
        {
            if (session != selectedIn)
                session.SelectedPeer = null;
        }
    }

    // Raised on the socket thread
    private void OnHybridSessionReconfigured(HybridSessionModel model)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var session = Sessions.OfType<HybridSessionVM>().FirstOrDefault(s => s.Model.Id == model.Id && !s.IsLeaving);

            if (session == null)
                return;

            session.Model = model;
            // The host may have changed
            session.ApplyAccess(Access.Permissions);
        });
    }

    // Raised on the socket thread
    private void OnChatSessionReconfigured(ChatSessionModel model)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Sessions.OfType<ChatSessionVM>().FirstOrDefault(s => s.Model.Id == model.Id && !s.IsLeaving) is not
                { } session)
                return;

            session.Model = model;
            // Its overrides may have changed
            session.ApplyAccess(Access.Permissions);
        });
    }

    private void RemoveChatSession(int sessionId)
    {
        if (CoordinatorInstanceId is { } coordinatorInstanceId)
            _ = _preferences.SaveChatMuteAsync(coordinatorInstanceId, sessionId, false);

        RemoveSession(sessionId);
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
            RebuildRows();
        });
    }
    
    /// <summary>
    /// The sessions of the list by their id, of one kind.
    /// </summary>
    private T? FindSession<T>(int sessionId) where T : class, ISessionVM =>
        Sessions.OfType<T>().FirstOrDefault(session => session.Model.Id == sessionId);

    /// <summary>
    /// Applies a coordinator notification to a hybrid session of the list on the UI thread, behind
    /// the notifications before it: the join of a peer comes before its leave and its state.
    /// </summary>
    private void PostToHybrid(int sessionId, Action<HybridSessionVM> apply) => Dispatcher.UIThread.Post(() =>
    {
        if (FindSession<HybridSessionVM>(sessionId) is { } session)
            apply(session);
    });

    private void AddPeerToSession(PeerJoinedEvent notification)
    {
        var coordinatorSession = _coordinatorSession!;
        var peer = notification.Peer;

        Dispatcher.UIThread.Post(async () =>
        {
            if (FindSession<HybridSessionVM>(notification.SessionId) is not { } session)
                return;

            // Not awaited before adding: the leave and state changes of this peer are posted right behind
            session.AddPeerToSession(peer);
            _ = _playbackService.LoadAsync(coordinatorSession.GetCurrentUser().CoordinatorInstanceId, [peer.UserId]);
            session.UpdateUserModel(peer.UserId, await coordinatorSession.GetUser(peer.UserId));
        });
    }

    /// <summary>Shows again what the current user may do, after their roles changed.</summary>
    private void ApplyAccess()
    {
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

    private void OnUserUpdated(UserModel user) => Dispatcher.UIThread.Post(() =>
    {
        foreach (var session in Sessions.OfType<HybridSessionVM>())
            session.UpdateUserModel(user.Id, user);
    });

    private void RemovePeerFromSession(PeerLeftEvent notification) =>
        PostToHybrid(notification.SessionId, session => _ = session.RemovePeerFromSession(notification.UserId));

    // Raised on any thread, also by a slider of this very list
    // The lists of peers show the voice; the sound of a screen is set on its tile
    private void OnPlaybackChanged(int userId, PeerAudioChannel channel, PeerPlayback playback)
    {
        if (channel != PeerAudioChannel.Voice)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var session in Sessions.OfType<HybridSessionVM>())
                session.ApplyPlayback(userId, playback);
        });
    }

    // Raised on a timer thread of the transport of the session we are in
    private void OnSpeakingChanged(int sessionId, int userId, bool isSpeaking) =>
        PostToHybrid(sessionId, session => session.UpdateSpeaking(userId, isSpeaking));

    // Raised on any thread by the session we are in
    private void OnScreenWatchChanged(int sessionId, int userId, bool isWatching) =>
        PostToHybrid(sessionId, session => session.UpdateScreenWatch(userId, isWatching));

    private void UpdatePeerStatus(PeerStateChangedEvent notification) =>
        PostToHybrid(notification.SessionId, session => session.UpdatePeerStatus(notification.UserId, notification.State));

    // Raised on socket threads
    private void UpdatePeerMicrophone(PeerMicrophoneMuteChangedEvent notification) =>
        PostToHybrid(notification.SessionId, session => session.UpdatePeerMicrophone(notification.UserId, notification.IsMuted));

    // Raised on socket threads
    private void UpdatePeerSound(PeerSoundMuteChangedEvent notification) =>
        PostToHybrid(notification.SessionId, session => session.UpdatePeerSound(notification.UserId, notification.IsMuted));

    // Raised on socket threads
    private void UpdatePeerAfk(PeerAfkChangedEvent notification) =>
        PostToHybrid(notification.SessionId,
            session => session.UpdatePeerAfk(notification.UserId, notification.IsAfk, notification.Reason));

    // Raised on socket threads
    private void OnHybridSessionUpdated(HybridSessionPeerEvent notification)
    {
        switch (notification)
        {
            // The server unmutes a peer that stops its voice without saying so
            case PeerTrackStoppedEvent { SourceType: MediaSourceType.Microphone } stopped:
                UpdatePeerMicrophone(new PeerMicrophoneMuteChangedEvent(stopped.SessionId, stopped.UserId, false));
                break;

            case PeerScreenShareStartedEvent started:
                UpdatePeerScreenShare(started.SessionId, started.UserId, true);
                break;

            // A receiver that connects later is only sent the track
            case PeerTrackStartedEvent { Track.SourceType: MediaSourceType.Screen } started:
                UpdatePeerScreenShare(started.SessionId, started.UserId, true);
                break;

            case PeerTrackStoppedEvent { SourceType: MediaSourceType.Screen } stopped:
                UpdatePeerScreenShare(stopped.SessionId, stopped.UserId, false);
                break;
        }
    }

    private void UpdatePeerScreenShare(int sessionId, int userId, bool isActive) =>
        PostToHybrid(sessionId, session => session.UpdatePeerScreenShare(userId, isActive));
    
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