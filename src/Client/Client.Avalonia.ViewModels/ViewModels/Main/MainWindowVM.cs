using Client.Application.Push;
using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Client.Avalonia.Localization;
using Client.Services;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using ReactiveUI.Primitives;
using Shared.Enums;
using Client.Core.Push;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;
using Client.Core.Settings;
using Client.Core.Users;
using Client.Application.Chat;
using Client.Application.Coordinator;
using Client.Application.Coordinator.Users;
using Client.Application.Sessions.Hybrid;
using Client.Application.Settings;
using Client.ViewModels.Connections;
using Client.ViewModels.Dialogs;
using Result = Client.ViewModels.Dialogs.Result;
using Client.ViewModels.Notifications;
using Client.ViewModels.Profile;
using Client.ViewModels.ServerSettings;
using Client.ViewModels.Sessions;
using Client.ViewModels.Sessions.Chat;
using Client.ViewModels.Sessions.Create;
using Client.ViewModels.Sessions.Hybrid;
using Client.ViewModels.Sessions.List;
using Client.ViewModels.Settings;

namespace Client.ViewModels.Main;

/// <summary>
/// The main window: the connection to one coordinator, the list of its sessions and the session
/// shown. The connection itself is handled in MainWindowVM.Connection.cs.
/// </summary>
public partial class MainWindowVM : ReactiveObject
{
    public StatusLineVM StatusLine { get; } = new();

    public IMainWindowContentVM? CurrentContent
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            UpdateChatOnScreen();
        }
    }

    /// <summary>
    /// Set by the platform: the window has the focus, or on Android the application is in front.
    /// Only then is the shown chat read.
    /// </summary>
    public bool IsWindowActive
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            UpdateChatOnScreen();
        }
    } = true;

    /// <summary>
    /// The unread counts of the chats of the connection, shared by the list and the open chats.
    /// </summary>
    public ChatReadTracker ChatReads { get; } = new(TimeSpan.FromSeconds(1));

    // The chat last told that it is on screen
    private ActiveChatSessionVM? _chatOnScreen;

    private void UpdateChatOnScreen()
    {
        var chat = IsWindowActive ? CurrentContent as ActiveChatSessionVM : null;
        if (_chatOnScreen != null && _chatOnScreen != chat)
            _chatOnScreen.IsOnScreen = false;

        _chatOnScreen = chat;
        if (chat != null)
            chat.IsOnScreen = true;
    }

    /// <summary>
    /// Sessions of the current connection; null while not connected.
    /// </summary>
    public OpenedSessions? OpenedSessions
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The hybrid session we are in, shown or not. The window answers its dialogs, so that they
    /// open whatever the content is.
    /// </summary>
    public ActiveHybridSessionVM? HybridSession
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public SessionsListVM SessionsList { get; }

    /// <summary>
    /// What the corner of the window lists.
    /// </summary>
    public NotificationCenterVM Notifications { get; }

    /// <summary>What the current user may do on the server, after their roles.</summary>
    public AccessVM Access { get; } = new();

    public ServerTitleVM ServerTitle { get; }

    public UserCardVM UserCard { get; }

    /// <summary>The updater menu of the top bar.</summary>
    public UpdaterVM Updater { get; }

    public bool IsConnected
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsInitialized
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(MenuVisibility));
        }
    }

    /// <summary>
    /// The shown session takes the whole screen: the window shows nothing of its own around it.
    /// Set from both sides: by the fullscreen command, and by the view when fullscreen is left
    /// by means of the system.
    /// </summary>
    public bool IsContentFullScreen
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(MenuVisibility));
        }
    }

    public bool GridOpacity
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool MenuVisibility => IsInitialized && !IsContentFullScreen;

    public double MenuOpacity
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = 0.6;

    public UserModel? User
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// Round trip to the server shown in the status bar; null hides it.
    /// </summary>
    public TimeSpan? ServerRoundTrip
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    // UI Interactions
    public Interaction<SettingsWindowVM, RxVoid> ShowSettings { get; } = new();
    public Interaction<ServerSettingsVM, RxVoid> ShowServerSettings { get; } = new();
    public Interaction<ConnectWindowVM, ICoordinatorSession?> ShowConnectWindow { get; } = new();
    public Interaction<MessageBoxVM, Result> ShowMessageBox { get; } = new();
    public Interaction<DatabasePasswordVM, bool> AskDatabasePassword { get; } = new();
    public Interaction<CreateSessionWindowVM, RxVoid> ShowCreateSessionWindow { get; } = new();
    public Interaction<EditProfileWindowVM, RxVoid> ShowEditProfileWindow { get; } = new();
    public Interaction<UserProfileWindowVM, RxVoid> ShowUserProfileWindow { get; } = new();
    public Interaction<HybridSessionSettingsWindowVM, RxVoid> ShowHybridSessionSettingsWindow { get; } = new();

    // Reactive Commands
    public ReactiveCommand<RxVoid, RxVoid> OpenSettingsCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> OpenServerSettingsCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ConnectCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DisconnectCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CreateSessionCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleFullscreenCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ShowHybridSessionCommand { get; }

    private readonly ReactiveCommand<(int SessionId, string Name, int Capacity, bool IsHostMustApprove), ChangeSessionSettingsResult>
        _changeHybridSessionSettingsCommand;

    private readonly UserActions _users;
    private readonly ChatPushReporter _pushReporter;
    private readonly LastSessionKeeper _lastSession;

    // DI
    private readonly ICoordinatorFactory _coordinatorFactory;
    private readonly ApplicationLoader _applicationLoader;
    private readonly IBehaviorService _behaviorService;
    private readonly IAvatarService _avatars;
    private readonly PushRegistration _pushRegistration;
    private readonly ChatSessionServices _chatServices;
    private readonly Func<HybridSessionServices> _hybridServices;
    private readonly Func<ConnectWindowVM> _createConnectWindow;
    private readonly Func<SettingsWindowVM> _createSettings;
    private readonly ILogger<MainWindowVM> _logger;

    public MainWindowVM(
        IBehaviorService behaviorService,
        ICoordinatorFactory coordinatorFactory,
        ApplicationLoader applicationLoader,
        IAvatarService avatars,
        INotificationService notifications,
        IHotKeysService hotKeys,
        PushRegistration pushRegistration,
        IPeerPlaybackService playback,
        IVoiceActivityService voiceActivity,
        IScreenWatchService screenWatch,
        SessionListPreferences listPreferences,
        ChatSessionServices chatServices,
        Func<HybridSessionServices> hybridServices,
        LastSessionKeeper lastSession,
        UpdaterVM updater,
        ILoggerFactory loggerFactory,
        Func<ConnectWindowVM> createConnectWindow,
        Func<SettingsWindowVM> createSettings)
    {
        _behaviorService = behaviorService;
        _coordinatorFactory = coordinatorFactory;
        _applicationLoader = applicationLoader;
        _avatars = avatars;
        _pushRegistration = pushRegistration;
        _chatServices = chatServices;
        _hybridServices = hybridServices;
        _lastSession = lastSession;
        Updater = updater;
        _createConnectWindow = createConnectWindow;
        _createSettings = createSettings;
        _logger = loggerFactory.CreateLogger<MainWindowVM>();
        Notifications = new NotificationCenterVM(notifications);
        hotKeys.Triggered += OnHotKey;

        var isConnected = this.WatchProperty(nameof(IsConnected), vm => vm.IsConnected);
        _users = new UserActions(() => _coordinatorSession, () => User, isConnected, avatars, notifications, Access,
            ShowMessageBox, ShowEditProfileWindow, ShowUserProfileWindow);

        OpenSettingsCommand = ReactiveCommand.CreateFromTask(async () => await ShowSettings.Handle(_createSettings()));
        OpenServerSettingsCommand = ReactiveCommand.CreateFromTask(OpenServerSettings);
        ConnectCommand = ReactiveCommand.CreateFromTask(ConnectToCoordinator);
        DisconnectCommand = ReactiveCommand.CreateFromTask(Disconnect);
        CreateSessionCommand = ReactiveCommand.CreateFromTask(() => CreateNewSession(null));
        ToggleFullscreenCommand = ReactiveCommand.Create(() => { IsContentFullScreen = !IsContentFullScreen; });
        ShowHybridSessionCommand = ReactiveCommand.Create(() => ChangeContent(OpenedSessions?.HybridSession));
        _changeHybridSessionSettingsCommand = ReactiveCommand
            .CreateFromTask<(int SessionId, string Name, int Capacity, bool IsHostMustApprove), ChangeSessionSettingsResult>(
                change => _coordinatorSession!.ChangeHybridSessionSettings(change.SessionId, change.Name,
                    change.Capacity, change.IsHostMustApprove));

        // Leaves only the session asked for: the menu may have been opened before we moved to another one
        var leaveHybridSessionCommand = ReactiveCommand.Create<int>(sessionId =>
        {
            if (HybridSession is { } session && session.Model.Id == sessionId)
                session.CloseSession();
        });

        SessionsList = new SessionsListVM(ConnectCommand,
            ReactiveCommand.Create<SessionModel>(OpenSession),
            leaveHybridSessionCommand,
            ReactiveCommand.CreateFromTask<SessionModel>(EditHybridSession, isConnected),
            _users.ViewProfileCommand, _users.KickPeerCommand,
            ReactiveCommand.CreateFromTask<int?>(CreateNewSession),
            playback, voiceActivity, screenWatch, listPreferences,
            ChatReads,
            Access,
            loggerFactory.CreateLogger<SessionsListVM>());
        ServerTitle = new ServerTitleVM(CreateSessionCommand, SessionsList.CreateCategoryCommand,
            OpenServerSettingsCommand, DisconnectCommand, notifications, Access);
        UserCard = new UserCardVM(_users.EditProfileCommand, _users.ViewProfileCommand, _users.SetStatusCommand);

        _pushReporter = new ChatPushReporter(
            () => IsConnected && _coordinatorSession is { CoordinatorModel.Id: { } id } session ? (session, id) : null,
            chatId => SessionsList.Sessions.OfType<ChatSessionVM>().FirstOrDefault(chat => chat.Model.Id == chatId),
            loggerFactory.CreateLogger<ChatPushReporter>());
    }

    /// <summary>
    /// Raised on the UI thread with a message somebody else sent to a chat that is not muted, in
    /// the form a push carries, for a platform that notifies about it while off the screen.
    /// </summary>
    public event Action<PushPayload>? ChatMessageReceived
    {
        add => _pushReporter.Reported += value;
        remove => _pushReporter.Reported -= value;
    }

    /// <summary>
    /// A hot key acts on the hybrid session we are in, as its button does, and on nothing without one.
    /// </summary>
    private void OnHotKey(HotKeyAction action)
    {
        if (HybridSession is not { } session)
            return;

        System.Windows.Input.ICommand command = action switch
        {
            HotKeyAction.ToggleMicrophone => session.StatusBar.ToggleMicrophoneCommand,
            HotKeyAction.ToggleSound => session.StatusBar.ToggleSoundCommand,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        if (command.CanExecute(null))
            command.Execute(null);
    }

    public async Task Initialize()
    {
        await StatusLine.SetAsync(Strings.Get("main.status.loading"), true);

        var result = await _applicationLoader.InitializeAsync(async dialog => await AskDatabasePassword.Handle(dialog));

        // The user chose to leave instead of typing the password
        if (result == ApplicationLoadResult.DatabaseLocked)
        {
            Environment.Exit(0);
            return;
        }

        if (result != ApplicationLoadResult.NoError)
        {
            await ShowMessageBox.Handle(new MessageBoxVM(Icon.Fatal, Buttons.Ok,
                GetInitializationErrorMessage(result), Strings.Get("main.startup.title")));

            Environment.Exit(-1);
            return;
        }

        await StatusLine.SetAsync(Strings.Get("main.status.offline"), false);

        IsInitialized = true;
        GridOpacity = true;
        MenuOpacity = 1;

        // Not awaited: a slow network must not hold the auto connection back
        _ = Updater.CheckOnStartupAsync();

        if ((await _behaviorService.GetSettingsAsync()).IsAutoConnectEnabled
            && await _behaviorService.GetAutoConnectInstance() is { } autoConnect)
            await AutoConnectToCoordinator(autoConnect.Coordinator, autoConnect.User);
    }

    private static string GetInitializationErrorMessage(ApplicationLoadResult result)
    {
        var reason = result switch
        {
            ApplicationLoadResult.UnexpectedError => "main.startup.unexpectedError",
            ApplicationLoadResult.DatabaseError => "main.startup.databaseError",
            ApplicationLoadResult.DatabaseCorrupted => "main.startup.databaseCorrupted",
            ApplicationLoadResult.DatabaseNewerThanApplication => "main.startup.databaseNewer",
            ApplicationLoadResult.DatabaseInUse => "main.startup.databaseInUse",
            ApplicationLoadResult.NativeAssembliesFailedToLoad => "main.startup.nativeLibraries",
            ApplicationLoadResult.MediaComponentsBroken => "main.startup.mediaComponents",
            ApplicationLoadResult.NativeLibraryFail => "main.startup.nativeLibrary",
            _ => "main.startup.internalError"
        };

        return Strings.Format("main.startup.failed", ("reason", Strings.Get(reason)));
    }

    private ActiveHybridSessionVM CreateHybridSession(HybridSessionModel sessionModel) =>
        new(sessionModel, _coordinatorSession!, _hybridServices(), ToggleFullscreenCommand, _users.KickPeerCommand);

    private void OnSessionClosed(IMainWindowContentVM session)
    {
        if (CurrentContent == session)
            ChangeContent(null);
    }

    private void OnHybridSessionChanged(ActiveHybridSessionVM? session)
    {
        HybridSession = session;
        SessionsList.SetEnteredHybridSession(session?.Model.Id);
        RememberLastSession();
    }

    /// <param name="categoryId">The category the new session goes into, or null for none.</param>
    private async Task CreateNewSession(int? categoryId)
    {
        using var vm = new CreateSessionWindowVM(_coordinatorSession!, categoryId: categoryId);
        await ShowCreateSessionWindow.Handle(vm);
    }

    private async Task OpenServerSettings()
    {
        if (_coordinatorSession == null)
            return;

        using var vm = new ServerSettingsVM(_coordinatorSession, ServerTitle, _users.ViewProfileCommand);
        await ShowServerSettings.Handle(vm);
    }

    private async Task EditHybridSession(SessionModel session)
    {
        if (session is not HybridSessionModel hybridSession)
            return;

        using var vm = new HybridSessionSettingsWindowVM(hybridSession, _changeHybridSessionSettingsCommand);
        await ShowHybridSessionSettingsWindow.Handle(vm);
    }

    private void OpenSession(SessionModel sessionModel)
    {
        var session = OpenedSessions?.Open(sessionModel);
        if (session != null)
            ChangeContent(session);
    }

    /// <summary>
    /// The coordinator we are connected to, or null.
    /// </summary>
    public string? ConnectedCoordinatorId => IsConnected ? _coordinatorSession?.CoordinatorModel?.Id : null;

    /// <summary>
    /// Shows a chat of the connected coordinator, for a notification the user tapped.
    /// </summary>
    /// <returns>False when another coordinator is connected, none is, or the chat is not in its list.</returns>
    public bool TryShowChat(string coordinatorInstanceId, int chatId)
    {
        if (ConnectedCoordinatorId != coordinatorInstanceId)
            return false;

        var chat = SessionsList.Sessions.FirstOrDefault(session => session is ChatSessionVM && session.Model.Id == chatId);
        if (chat == null)
            return false;

        OpenSession(chat.Model);
        return true;
    }

    /// <summary>
    /// Only switches what is shown: the content belongs to <see cref="OpenedSessions"/>,
    /// so hiding it never ends a session.
    /// </summary>
    private void ChangeContent(IMainWindowContentVM? content)
    {
        // Fullscreen belongs to the shown session, the window would stay without its menu
        if (content == null)
            ExitFullScreen();

        CurrentContent = content;

        switch (content)
        {
            case ActiveChatSessionVM chat:
                SessionsList.SetShownSession(chat.Id, false);
                break;
            case ActiveHybridSessionVM hybrid:
                SessionsList.SetShownSession(hybrid.Model.Id, true);
                break;
            default:
                SessionsList.SetShownSession(null, false);
                break;
        }

        RememberLastSession();
    }

    public void ExitFullScreen() => IsContentFullScreen = false;
}
