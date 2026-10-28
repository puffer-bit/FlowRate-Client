using Client.Services;
using Client.Avalonia.Localization;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReactiveUI;
using ReactiveUI.Primitives;
using Shared.Enums;
using Client.Core.Coordinator;
using Client.Core.Events;
using Client.Core.Sessions.Hybrid;
using Client.Core.Users;
using Client.Application.Coordinator;
using Client.Application.Coordinator.Users;
using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Application.Sessions.Hybrid;
using Client.Application.Settings;
using Client.Application.Sounds;
using Client.ViewModels.Dialogs;
using Result = Client.ViewModels.Dialogs.Result;
using Client.ViewModels.Main;
using Client.ViewModels.Notifications;

namespace Client.ViewModels.Sessions.Hybrid;

public partial class ActiveHybridSessionVM : ReactiveObject, IMainWindowContentVM
{
    public HybridSessionModel Model
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsUserListVisible
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    private bool IsClosed { get; set; }

    /// <summary>
    /// The WebRTC transport is up, so tracks can be sent.
    /// </summary>
    public bool IsTransportConnected
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The server let the current user in: the session needs no approval, the user hosts it or the host approved.
    /// Until then nothing is sent or received.
    /// </summary>
    public bool IsAdmitted
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The roles of the current user let it send voice in the session.</summary>
    public bool CanSpeak
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The roles of the current user let it share the screen in the session.</summary>
    public bool CanShareVideo
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public HybridSessionStatusBarVM StatusBar { get; set; }
    
    public HybridSessionParticipantsListVM ParticipantsListVM { get; set; }
    public ObservableCollection<NotificationPopUpVM> NotificationsListViewModel { get; } = new();
    public HybridSessionBitmapFlyoutVM BitmapFlyoutVM { get; set; }

    public Interaction<MessageBoxVM, Result> ShowMessageBox { get; }
    public Interaction<VideoSourceSelectWindowVM, ScreenShareRequest?> SelectSources { get; set; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleFullscreenCommand { get; }
    public ReactiveCommand<bool, RxVoid> ToggleUserListCommand { get; }
    
    /// <summary>
    /// The screens shown in the session, one per user who shows one. None is watched until asked for.
    /// </summary>
    public ObservableCollection<ScreenStreamVM> ScreenStreams { get; } = new();

    /// <summary>
    /// The session has to end: the user left it or joining failed.
    /// The owner disposes the view model, which leaves the session on the server.
    /// </summary>
    public event Action<ActiveHybridSessionVM>? Closed;

    private readonly CompositeDisposable _disposables = new();
    private readonly EventSubscriptions _subscriptions = new();

    // Set while the tracks are restarted for new media settings, and when they are to be once more. UI thread only
    private bool _isRestartingMedia;
    private bool _isMediaRestartPending;
    
    private readonly ICoordinatorSession _coordinatorSession;
    private readonly IScreenCastClient? _screenCastClient;
    private readonly IAudioPlayerService _audioPlayerService;
    private readonly IAudioService _audioService;
    private readonly ISoundService _sounds;
    private readonly INotificationService _notifications;
    private readonly IVoiceActivityService _voiceActivity;
    private readonly IScreenWatchService _screenWatch;
    private readonly IPeerPlaybackService _playbackService;
    private readonly IScreenSourceProvider? _screenSourceProvider;
    private readonly IAudioAppProvider? _audioApps;
    private readonly IMediaCapabilities _mediaCapabilities;
    private readonly IScreenShareSettingsRepository _screenShareSettings;
    private readonly IDismissedPromptsRepository _dismissedPrompts;
    private readonly ICoordinatorStateRepository _coordinatorStates;
    private IHybridSession _hybridSession;

    // Set once the server has let us in, so disposing knows there is a session to leave
    private bool _isJoined;
    
    public ActiveHybridSessionVM(
        HybridSessionModel hybridSessionModel, 
        ICoordinatorSession coordinatorSession,
        HybridSessionServices services,
        ReactiveCommand<RxVoid, RxVoid> toggleFullscreenCommand,
        ReactiveCommand<(int SessionId, int UserId), RxVoid> kickPeerCommand,
        ReactiveCommand<(int SessionId, int UserId, bool IsApproved), RxVoid> admitPeerCommand)
    {
        _coordinatorStates = services.CoordinatorStates;
        _mediaCapabilities = services.MediaCapabilities;
        _screenShareSettings = services.ScreenShareSettings;
        _dismissedPrompts = services.DismissedPrompts;
        _coordinatorSession = coordinatorSession;
        _audioPlayerService = services.AudioPlayer;
        _audioService = services.Audio;
        _sounds = services.Sounds;
        _notifications = services.Notifications;

        // An error of the session is heard as well as seen, wherever it is reported from
        NotificationsListViewModel.CollectionChanged += (_, e) =>
        {
            if (e.NewItems?.OfType<NotificationPopUpVM>().Any(item => item.Type == NotificationPopUpVM.PopUpType.Error) == true)
                _sounds.Play(SoundEvent.Error);
        };
        _voiceActivity = services.VoiceActivity;
        _screenWatch = services.ScreenWatch;
        _playbackService = services.Playback;
        Model = hybridSessionModel;
        _screenCastClient = services.CreateScreenCastClient?.Invoke();
        _screenSourceProvider = services.ScreenSources;
        _audioApps = services.AudioApps;

        var hybridSession = _hybridSession = services.SessionFactory.CreateHybridSession(Model, _coordinatorSession);
        _subscriptions
            .Add(() => _audioService.SettingsChanged += OnAudioSettingsChanged, () => _audioService.SettingsChanged -= OnAudioSettingsChanged)
            .Add(() => _voiceActivity.Changed += OnSpeakingChanged, () => _voiceActivity.Changed -= OnSpeakingChanged)
            .Add(() => hybridSession.ScreenShareChanged += OnScreenShareChanged,
                () => hybridSession.ScreenShareChanged -= OnScreenShareChanged)
            .Add(() => hybridSession.ScreenWatchChanged += OnScreenWatchChanged,
                () => hybridSession.ScreenWatchChanged -= OnScreenWatchChanged)
            .Add(() => hybridSession.VideoFrameReady += OnVideoFrame, () => hybridSession.VideoFrameReady -= OnVideoFrame)
            .Add(() => hybridSession.StatsSampled += OnStatsSampled, () => hybridSession.StatsSampled -= OnStatsSampled)
            .Add(() => _screenWatch.Requested += OnScreenWatchRequested, () => _screenWatch.Requested -= OnScreenWatchRequested)
            .Add(() => _playbackService.Changed += OnPlaybackChanged, () => _playbackService.Changed -= OnPlaybackChanged)
            .Add(() => coordinatorSession.HybridSessionReconfigured += OnSessionReconfigured,
                () => coordinatorSession.HybridSessionReconfigured -= OnSessionReconfigured);

        if (_screenCastClient is { } screenCastClient)
            _subscriptions.Add(() => screenCastClient.Closed += OnScreenCastClosed, () => screenCastClient.Closed -= OnScreenCastClosed);
        
        ShowMessageBox = new Interaction<MessageBoxVM, Result>();
        SelectSources = new Interaction<VideoSourceSelectWindowVM, ScreenShareRequest?>();
        
        ToggleFullscreenCommand = toggleFullscreenCommand;
        ToggleUserListCommand = ReactiveCommand
            .Create<bool>(ToggleUserList);
        
        StatusBar = new HybridSessionStatusBarVM(
            hybridSessionModel.Name, 
            ToggleUserListCommand,
            toggleFullscreenCommand, 
            ReactiveCommand.Create(CloseSession),
            ReactiveCommand.CreateFromTask(ToggleMicrophone,
                Observable.Select(this.WatchProperties(nameof(IsTransportConnected), nameof(IsAdmitted), nameof(CanSpeak)),
                    vm => vm.IsTransportConnected && vm.IsAdmitted && vm.CanSpeak)),
            ReactiveCommand.CreateFromTask(ToggleSound,
                this.WatchProperty(nameof(IsTransportConnected), vm => vm.IsTransportConnected)),
            ReactiveCommand.CreateFromTask(ToggleAfk,
                this.WatchProperty(nameof(IsTransportConnected), vm => vm.IsTransportConnected)),
            ReactiveCommand.CreateFromTask(ToggleScreenShare,
                Observable.Select(this.WatchProperties(nameof(IsTransportConnected), nameof(IsAdmitted),
                        nameof(CanShareVideo)),
                    vm => vm.IsTransportConnected && vm.IsAdmitted && vm.CanShareVideo)),
            ReactiveCommand.CreateFromTask(ChangeScreenShare,
                Observable.Select(this.WatchProperties(nameof(IsTransportConnected), nameof(IsAdmitted),
                        nameof(CanShareVideo)),
                    vm => vm.IsTransportConnected && vm.IsAdmitted && vm.CanShareVideo)));
        _subscriptions.Add(() => Strings.LanguageChanged += StatusBar.UpdateTexts,
            () => Strings.LanguageChanged -= StatusBar.UpdateTexts);
        // Corrected by our own peer as soon as the server reports it
        IsAdmitted = !hybridSessionModel.IsApprovalRequested
                     || hybridSessionModel.HostId == _coordinatorSession.GetCurrentUser().Id;
        ParticipantsListVM = new HybridSessionParticipantsListVM(_coordinatorSession.GetCurrentUser().Id,
            userId => ObservableExtensions.Subscribe(kickPeerCommand.Execute((Model.Id, userId))),
            userId => ObservableExtensions.Subscribe(admitPeerCommand.Execute((Model.Id, userId, true))),
            userId => ObservableExtensions.Subscribe(admitPeerCommand.Execute((Model.Id, userId, false))));
        ApplyAccess(_coordinatorSession.Permissions);
        _subscriptions.Add(() => coordinatorSession.PermissionsChanged += OnPermissionsChanged,
            () => coordinatorSession.PermissionsChanged -= OnPermissionsChanged);
        BitmapFlyoutVM = new HybridSessionBitmapFlyoutVM();

        _disposables.Add(
            ObservableExtensions.Subscribe(BitmapFlyoutVM.WatchProperty(nameof(HybridSessionBitmapFlyoutVM.Volume), x => x.Volume), newVolume =>
            {
                _audioPlayerService.ChangeVolume(newVolume);
            })
        );

        _ = JoinSession();
    }

    public async Task JoinSession()
    {
        await Task.Delay(200);
        if (IsClosed)
            return;

        try
        {
            var result = await _coordinatorSession.JoinHybridSession(Model.Id, _hybridSession.ClientEpoch);

            // Left before the server answered: nothing is set up yet, only the join itself is undone
            if (IsClosed)
            {
                if (result.Result.IsSuccess)
                    _ = _coordinatorSession.LeaveSession(Model.Id);
                return;
            }

            if (result.Result.IsSuccess && 
                _coordinatorSession.ConnectionStatus == CoordinatorState.Connected)
            {
                // Subscribed before the snapshot is taken, so that a peer joining or connecting in
                // between is not lost. Adding a peer twice is ignored by the list.
                var session = _coordinatorSession;
                _subscriptions
                    .Add(() => session.HybridSessionPeerJoined += OnPeerJoined, () => session.HybridSessionPeerJoined -= OnPeerJoined)
                    .Add(() => session.HybridSessionPeerLeft += OnPeerLeft, () => session.HybridSessionPeerLeft -= OnPeerLeft)
                    .Add(() => session.HybridSessionPeerStateChanged += OnPeerStateChanged,
                        () => session.HybridSessionPeerStateChanged -= OnPeerStateChanged)
                    .Add(() => session.UserUpdated += OnUserUpdated, () => session.UserUpdated -= OnUserUpdated)
                    .Add(() => session.HybridSessionPeerPoked += OnPoked, () => session.HybridSessionPeerPoked -= OnPoked);
                _isJoined = true;
                _sounds.Play(SoundEvent.Join);

                var getAllPeerResult = await _coordinatorSession!.GetAllHybridSessionPeers(Model.Id);
                if (!getAllPeerResult.Result.IsSuccess)
                    throw new Exception($"Failed to get Peers for HybridSession {Model.Name}");

                var peers = getAllPeerResult.Value!.Values;

                foreach (var peer in peers)
                {
                    ParticipantsListVM.AddPeerToSession(peer);
                    if (peer.UserId == _coordinatorSession.GetCurrentUser().Id)
                        await UpdateAdmissionAsync(peer.State);

                    // Screens that were shown before we came are known from the peers only
                    if (peer.IsScreenShareActive)
                        AddScreenStream(peer.UserId);
                }

                var users = await _coordinatorSession.GetUsers(peers.Select(peer => peer.UserId).ToList());

                foreach (var (userId, user) in users)
                {
                    ParticipantsListVM.UpdateUserModel(userId, user);
                    UpdateScreenStreamName(userId);
                }

                await HandleSessionStatus(_hybridSession.State);

                // Left while the peers were loading: disposing has taken the handlers off already
                if (IsClosed)
                    return;

                // Subscribed before connecting, so that an early failure is not lost
                var hybridSession = _hybridSession;
                _subscriptions
                    .Add(() => hybridSession.StateChanged += OnSessionStateChanged,
                        () => hybridSession.StateChanged -= OnSessionStateChanged)
                    .Add(() => session.HybridSessionPeerMicrophoneMuteChanged += HandleMicroState,
                        () => session.HybridSessionPeerMicrophoneMuteChanged -= HandleMicroState)
                    .Add(() => hybridSession.ErrorOccurred += OnSessionError, () => hybridSession.ErrorOccurred -= OnSessionError);
                
                // Off the UI thread: the first transport of a run loads the WebRTC plugins while
                // it builds its pipeline, and the window would stand still for that long
                await Task.Run(() => _hybridSession.Connect(result.Value!));

                if (IsClosed)
                    return;

                // Without a hardware encoder screen sharing falls back to software
                if (!_mediaCapabilities.HasHardwareScreenShareEncoder)
                    ShowPopUp(NotificationPopUpVM.PopUpType.Warning, Strings.Get("hybrid.hardwareAccelerationUnavailable"),
                        "HardwareAccelerationUnavailable");
            }
            else
            {
                // A rejection is reported by the main window
                if (!IsClosed && !result.Result.IsRejected)
                {
                    var message = result.Result.Error == ErrorCode.SFUUnavailable
                        ? Strings.Get("hybrid.join.noVoiceServer")
                        : Strings.Format("hybrid.join.failed", ("code", result.Result));

                    await ShowMessageBox.Handle(new MessageBoxVM(Icon.Error, Buttons.Ok, message,
                        Strings.Get("common.error")));
                }
                CloseSession();
            }
        }
        catch (TimeoutException)
        {
            if (!IsClosed)
            {
                await ShowMessageBox.Handle(new MessageBoxVM(Icon.Error, Buttons.Ok, 
                    Strings.Get("hybrid.join.timedOut"), 
                    Strings.Get("common.error")));
                _ = _coordinatorSession.LeaveSession(Model.Id);
                CloseSession();
            }
        }
    }

    public void CloseSession()
    {
        if (!IsClosed)
            Closed?.Invoke(this);
    }
    
    private async Task HandleSessionStatus(HybridSessionState state)
    {
        StatusBar.ChangeStatus(state);
        IsTransportConnected = state == HybridSessionState.Connected;

        // A transport that went down takes the outgoing tracks with it
        if (state is HybridSessionState.Failed or HybridSessionState.Disconnected or HybridSessionState.Reconnecting
            or HybridSessionState.WaitingForSfuNode)
        {
            StatusBar.IsMicrophoneOn = false;
            StatusBar.IsScreenShareOn = false;
            ForgetMicrophoneInput();
        }

        if (state != HybridSessionState.Connected)
            return;

        await StartMediaAsync();
    }

    /// <summary>
    /// Sends and receives what the user had on. Waits while the host has not let the user in: the server
    /// refuses media until then, and the approval starts it.
    /// </summary>
    private async Task StartMediaAsync()
    {
        if (!IsAdmitted)
            return;

        await RestoreAfkAsync();

        if (CanSpeak)
            await StartVoiceAsync();

        // The source is still granted, so a screen that was shared before the reconnect comes back
        if (!IsClosed && _screenInput is { } screenInput && _screenSettings is { } screenSettings
            && !_hybridSession.IsScreenShareStarted)
            await StartScreenShareAsync(screenInput, screenSettings, _screenAudioInput);

        // The new transport receives none of the screens the old one did
        if (!IsClosed)
            await RestoreWatchedScreensAsync();
    }
    
    /// <summary>
    /// Starts our running tracks again, so that they follow the media settings the session was just
    /// given: the server puts them into a track only when it starts. The others restart theirs the
    /// same way, and the screens that were watched are watched again when they come back.
    /// </summary>
    private async Task RestartMediaAsync()
    {
        RememberRestartingScreens();

        // A change that comes while the tracks restart is taken by one more round
        _isMediaRestartPending = true;

        if (_isRestartingMedia)
            return;

        _isRestartingMedia = true;

        try
        {
            // A transport that is not up has no tracks, and the next connection starts them with the new settings
            while (_isMediaRestartPending && !IsClosed && IsTransportConnected && IsAdmitted)
            {
                _isMediaRestartPending = false;
                await RestartTracksAsync();
            }
        }
        finally
        {
            _isRestartingMedia = false;
            _isMediaRestartPending = false;
        }
    }

    private async Task RestartTracksAsync()
    {
        if (_hybridSession.IsVoiceStarted)
        {
            // The microphone comes back muted or not as the user left it
            await _hybridSession.StopVoiceShareAsync();
            ForgetMicrophoneInput();
            StatusBar.IsMicrophoneOn = false;

            if (!IsClosed && CanSpeak)
                await StartVoiceAsync();
        }

        // The source stays granted, only the track it is sent on is replaced
        if (!IsClosed && _hybridSession.IsScreenShareStarted && _screenInput is { } screenInput
            && _screenSettings is { } screenSettings)
        {
            var audioInput = _screenAudioInput;

            await _hybridSession.StopScreenShareAsync();

            if (!IsClosed)
                await StartScreenShareAsync(screenInput, screenSettings, audioInput, isSilent: true);
        }
    }

    // The session reports its state and errors from GStreamer and thread pool threads, so both are
    // marshalled to the UI thread before they touch any view model
    private void OnStatsSampled(TransportStatsSample sample) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (IsClosed)
                return;

            StatusBar.Stats.Add(sample);

            var now = TimeSpan.FromMilliseconds(Environment.TickCount64);

            foreach (var stream in ScreenStreams)
                stream.Analyze(sample, _hybridSession.HasWatchedScreenAudio(stream.UserId), now);
        });

    private void OnSessionStateChanged(HybridSessionState state) =>
        Dispatcher.UIThread.Post(async () => await HandleSessionStatus(state));

    private void OnSessionError(HybridSessionError error) =>
        Dispatcher.UIThread.Post(() => ShowPopUp(NotificationPopUpVM.PopUpType.Error, HybridSessionMessages.Get(error),
            "GstWebRTCError"));

    private void ShowPopUp(NotificationPopUpVM.PopUpType type, string text, string tag) =>
        NotificationsListViewModel.Add(new NotificationPopUpVM(type, text, NotificationsListViewModel, tag));

    // Coordinator notifications arrive on the socket thread, so they are marshalled to the UI thread
    private void OnPeerJoined(PeerJoinedEvent notification)
    {
        if (notification.SessionId != Model.Id)
            return;

        Dispatcher.UIThread.Post(async () =>
        {
            var peer = notification.Peer;

            // We are announced again after every rejoin, with the admission the server kept
            if (peer.UserId != _coordinatorSession.GetCurrentUser().Id)
                _sounds.Play(SoundEvent.Join);
            else
                await UpdateAdmissionAsync(peer.State);

            ParticipantsListVM.AddPeerToSession(peer);
            ParticipantsListVM.UpdateUserModel(peer.UserId, await _coordinatorSession.GetUser(peer.UserId));
            UpdateScreenStreamName(peer.UserId);
        });
    }

    // Raised on the socket thread
    private void OnSessionReconfigured(HybridSessionModel model)
    {
        if (model.Id != Model.Id)
            return;

        Dispatcher.UIThread.Post(async () =>
        {
            var isMediaChanged = model.Media != Model.Media;

            Model = model;
            StatusBar.Rename(model.Name);
            // The host may have changed
            ApplyAccess(_coordinatorSession.Permissions);

            if (isMediaChanged)
                await RestartMediaAsync();
        });
    }

    // Raised on the transport thread
    private void OnPermissionsChanged(CoordinatorPermissions permissions) =>
        Dispatcher.UIThread.Post(() => ApplyAccess(permissions));

    private void ApplyAccess(CoordinatorPermissions permissions)
    {
        var granted = permissions.InSession(Model);
        CanSpeak = granted.HasFlag(Permission.Speak);
        CanShareVideo = granted.HasFlag(Permission.ShareVideo);
        ParticipantsListVM.ApplyAccess(permissions, Model.HostId == _coordinatorSession.GetCurrentUser().Id);
    }

    private void OnUserUpdated(UserModel user)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ParticipantsListVM.UpdateUserModel(user.Id, user);
            UpdateScreenStreamName(user.Id);
        });
    }

    private void OnPeerLeft(PeerLeftEvent notification)
    {
        if (notification.SessionId != Model.Id)
            return;

        if (notification.UserId == _coordinatorSession.GetCurrentUser().Id)
        {
            if (notification is PeerKickedEvent kicked)
                Dispatcher.UIThread.Post(() => _ = OnKicked(kicked));
            else if (notification is PeerRejectedEvent rejected)
                Dispatcher.UIThread.Post(() => _ = OnRejected(rejected));
            return;
        }

        _sounds.Play(notification is PeerKickedEvent or PeerRejectedEvent ? SoundEvent.UserKicked : SoundEvent.Leave);

        Dispatcher.UIThread.Post(() => ParticipantsListVM.RemovePeerFromSession(notification.UserId));
    }

    // The server has already taken us out, so the session is closed without leaving it
    private async Task OnKicked(PeerKickedEvent kicked)
    {
        if (IsClosed)
            return;

        _isJoined = false;
        _sounds.Play(SoundEvent.UserKicked);
        CloseSession();

        if (kicked.InitiatorUserId is not { } initiatorId)
        {
            _notifications.Show(NotificationType.Warning, Strings.Get("hybrid.kicked.title"),
                Strings.Format("hybrid.kickedByServer.text", ("session", Model.Name), ("reason", kicked.Reason ?? "")));
            return;
        }

        var initiator = await _coordinatorSession.GetUser(initiatorId);
        _notifications.Show(NotificationType.Warning, Strings.Get("hybrid.kicked.title"),
            Strings.Format("hybrid.kicked.text", ("session", Model.Name),
                ("user", initiator?.Username ?? initiatorId.ToString())));
    }

    // The server took us out while we waited, so the session is closed without leaving it
    private async Task OnRejected(PeerRejectedEvent rejected)
    {
        if (IsClosed)
            return;

        _isJoined = false;
        _sounds.Play(SoundEvent.UserKicked);
        CloseSession();

        var initiator = rejected.InitiatorUserId is { } initiatorId
            ? (await _coordinatorSession.GetUser(initiatorId))?.Username ?? initiatorId.ToString()
            : "";
        _notifications.Show(NotificationType.Warning, Strings.Get("hybrid.rejected.title"),
            Strings.Format("hybrid.rejected.text", ("session", Model.Name), ("user", initiator)));
    }

    private void OnPeerStateChanged(PeerStateChangedEvent notification)
    {
        if (notification.SessionId != Model.Id)
            return;

        Dispatcher.UIThread.Post(async () =>
        {
            ParticipantsListVM.UpdatePeerStatus(notification.UserId, notification.State);
            if (notification.UserId == _coordinatorSession.GetCurrentUser().Id)
                await UpdateAdmissionAsync(notification.State);
        });
    }

    private async Task UpdateAdmissionAsync(HybridSessionPeerState state)
    {
        var wasAdmitted = IsAdmitted;
        IsAdmitted = state != HybridSessionPeerState.WaitingForApprove;

        // A transport that is already up waited only for the approval
        if (!wasAdmitted && IsAdmitted && IsTransportConnected && !IsClosed)
            await StartMediaAsync();
    }

    // Raised on a timer thread of the transport
    private void OnSpeakingChanged(int sessionId, int userId, bool isSpeaking)
    {
        if (sessionId != Model.Id)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            ParticipantsListVM.UpdateSpeaking(userId, isSpeaking);
            if (userId == _coordinatorSession.GetCurrentUser().Id)
                StatusBar.IsSpeaking = isSpeaking;
        });
    }

    private void ToggleUserList(bool isToggled)
    {
        IsUserListVisible = isToggled;
    }
    
    public void Dispose()
    {
        if (IsClosed)
            return;
        IsClosed = true;

        _subscriptions.Dispose();

        if (_isJoined)
        {
            _ = _coordinatorSession.LeaveSession(Model.Id);
            _sounds.Play(SoundEvent.Leave);
        }

        _hybridSession?.Dispose();
        _disposables.Dispose();

        if (_screenInput is PipeWireVideoInput pipeWire)
            pipeWire.Remote.Dispose();

        _screenInput = null;
        _screenSettings = null;
        _screenAudioInput = null;
        _screenCastClient?.Dispose();
    }
}