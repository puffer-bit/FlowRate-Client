using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Avalonia.Localization;
using Client.Core.Coordinator;
using Client.Core.Settings;
using Client.Core.Users;
using Client.Services;
using Client.ViewModels.Dialogs;
using Client.ViewModels.Sessions;
using Client.ViewModels.Sessions.Chat;
using Client.ViewModels.Sessions.Hybrid;
using Client.ViewModels.Sessions.List;
using Result = Client.ViewModels.Dialogs.Result;

namespace Client.ViewModels.Main;

public partial class MainWindowVM
{
    private ICoordinatorSession? _coordinatorSession;
    private CancellationTokenSource _connectionCancellationSource = new();
    private readonly EventSubscriptions _coordinatorSubscriptions = new();

    // Counts the loads of the coordinator data; a load outlived by a loss or a disconnect stops at its next step
    private int _restoreId;

    // Rejections come in bursts, e.g. every request of a rate limited action; one message covers a burst
    private static readonly TimeSpan RejectionMessagePause = TimeSpan.FromSeconds(5);
    private bool _isRejectionMessageShown;
    private DateTime _nextRejectionMessageTime;

    private Task SetStatus(string key, bool isActive) => StatusLine.SetAsync(Strings.Get(key), isActive);

    private async Task AutoConnectToCoordinator(SavedCoordinatorModel coordinator, SavedUserModel user)
    {
        var connecting = Strings.Format("main.status.connecting", ("host", coordinator.Address),
            ("port", coordinator.Port), ("user", user.Username));

        await StatusLine.SetAsync(connecting, true);
        var session = _coordinatorFactory.CreateCoordinatorSession();
        await session.Connect(coordinator.Address, coordinator.Port, user.UserIdentity, user.Password);

        if (session.ConnectionStatus == CoordinatorState.CertificateNotTrusted)
        {
            if (!await CertificateTrustMessages.ConfirmTrustAsync(session, ShowMessageBox))
            {
                await SetStatus("main.status.offline", false);
                session.Dispose();
                return;
            }

            await StatusLine.SetAsync(connecting, true);
            await session.Connect(coordinator.Address, coordinator.Port, user.UserIdentity, user.Password);
        }

        if (session.ConnectionStatus != CoordinatorState.Connected)
        {
            await SetStatus("main.status.connectionFailed", false);
            await ShowMessageBox.Handle(session.ConnectionStatus == CoordinatorState.CoordinatorMismatch
                ? CertificateTrustMessages.CreateMismatchMessage(session.CoordinatorMismatch)
                : new MessageBoxVM(Icon.Error, Buttons.Ok,
                    Strings.Get("main.autoConnect.failed"), Strings.Get("main.autoConnect.failedTitle")));
            session.Dispose();
            return;
        }

        _coordinatorSession = session;
        await FinalizeCoordinatorConnection();
    }

    private async Task ConnectToCoordinator()
    {
        if (_coordinatorSession != null &&
            _coordinatorSession.ConnectionStatus != CoordinatorState.Connected)
        {
            await _connectionCancellationSource.CancelAsync();
            UnsubscribeFromCoordinator();
            _coordinatorSession.Dispose();
            _coordinatorSession = null;
            _connectionCancellationSource = new();
        }

        var newCoordinator = await ShowConnectWindow.Handle(_createConnectWindow());
        if (newCoordinator is { ConnectionStatus: CoordinatorState.Connected })
        {
            _coordinatorSession = newCoordinator;
            await FinalizeCoordinatorConnection();
        }
    }

    // Asked for by the user: the server left on purpose must not notify this device any more
    private async Task Disconnect()
    {
        if (_coordinatorSession != null)
            await _pushRegistration.UnregisterAsync(_coordinatorSession);

        var session = _coordinatorSession;
        await DisconnectFromCoordinator();
        await SetStatus("main.status.offline", false);
        session?.Dispose();

        // A connect may have replaced it meanwhile
        if (_coordinatorSession == session)
            _coordinatorSession = null;
    }

    private async Task FinalizeCoordinatorConnection()
    {
        var session = _coordinatorSession!;
        _coordinatorSubscriptions
            .Add(() => session.StateChanged += OnConnectionStateChanged, () => session.StateChanged -= OnConnectionStateChanged)
            .Add(() => session.ReconnectScheduled += OnReconnectScheduled, () => session.ReconnectScheduled -= OnReconnectScheduled)
            .Add(() => session.RequestRejected += OnRequestRejected, () => session.RequestRejected -= OnRequestRejected)
            .Add(() => session.PermissionsChanged += OnPermissionsChanged, () => session.PermissionsChanged -= OnPermissionsChanged)
            .Add(() => session.CurrentUserChanged += OnCurrentUserChanged, () => session.CurrentUserChanged -= OnCurrentUserChanged)
            .Add(() => session.ServerRoundTripChanged += OnServerRoundTripChanged,
                () => session.ServerRoundTripChanged -= OnServerRoundTripChanged)
            .Add(() => session.TextMessageAdded += _pushReporter.OnTextMessageAdded,
                () => session.TextMessageAdded -= _pushReporter.OnTextMessageAdded)
            .Add(() => _avatars.Attach(session), () => _avatars.Attach(null))
            .Add(() => ServerRoundTrip = session.ServerRoundTrip, () => ServerRoundTrip = null);

        // A connect the user asked for starts from nothing, whatever an earlier run has left
        _lastSession.Forget(session.CoordinatorModel?.Id);

        await RestoreCoordinatorData();
    }

    private void UnsubscribeFromCoordinator() => _coordinatorSubscriptions.Clear();

    private async Task RestoreCoordinatorData()
    {
        if (_coordinatorSession is not { } session)
            return;

        var restoreId = ++_restoreId;
        IsConnected = true;
        User = session.GetCurrentUser();
        Access.Update(session.Permissions);
        _avatars.Attach(session);
        UserCard.AttachUser(User);

        await SetStatus("main.status.retrievingSessions", true);
        if (!IsRestoreCurrent(session, restoreId))
            return;

        SessionsList.ShowLoading();
        await Task.Delay(700);
        if (IsRestoreCurrent(session, restoreId))
            await AttachSessionManager(session, restoreId);
    }

    // The connection may be lost or closed at any await of a load: what comes after is somebody else's
    private bool IsRestoreCurrent(ICoordinatorSession session, int restoreId) =>
        restoreId == _restoreId && _coordinatorSession == session && session.ConnectionStatus == CoordinatorState.Connected;

    /// <summary>
    /// Saves what is on screen and the hybrid session we are in, to return there once a lost
    /// connection is restored.
    /// </summary>
    private void RememberLastSession()
    {
        if (!IsConnected
            || _coordinatorSession is not { ConnectionStatus: CoordinatorState.Connected, CoordinatorModel.Id: var coordinatorId })
            return;

        _lastSession.Remember(coordinatorId, new LastSession((CurrentContent as ActiveChatSessionVM)?.Id,
            CurrentContent is ActiveHybridSessionVM, OpenedSessions?.HybridSession?.Model.Id));
    }

    /// <summary>
    /// Enters the sessions the lost connection has taken us out of: joins the hybrid session again
    /// and shows what was on screen. A screen that was shared is not shared again.
    /// </summary>
    private async Task RestoreLastSessionAsync()
    {
        if (!_lastSession.IsKept)
            return;

        var coordinatorSession = _coordinatorSession;
        var last = coordinatorSession?.CoordinatorModel?.Id is { } coordinatorId
            ? await _lastSession.ReadAsync(coordinatorId)
            : null;

        // Lost again or left while the database answered
        if (!_lastSession.IsKept || coordinatorSession != _coordinatorSession || OpenedSessions is not { } sessions
            || coordinatorSession?.ConnectionStatus != CoordinatorState.Connected)
            return;

        _lastSession.IsKept = false;

        if (last != null)
        {
            var hybrid = last.HybridSessionId is { } hybridId
                ? SessionsList.Sessions.FirstOrDefault(item => item is HybridSessionVM && item.Model.Id == hybridId)
                : null;
            var joined = hybrid != null ? sessions.Open(hybrid.Model) : null;

            // An opened chat stays on screen through the reconnect, and the user may have opened another meanwhile
            if (CurrentContent == null)
            {
                if (last.IsHybridSessionShown && joined != null)
                    ChangeContent(joined);
                else if (last.ShownChatId is { } chatId)
                    TryShowChat(coordinatorSession.CoordinatorModel!.Id, chatId);
            }
        }

        // The sessions that are gone by now are not returned to after the next loss
        RememberLastSession();
    }

    // Raised on the transport thread
    private void OnServerRoundTripChanged(TimeSpan? roundTrip) =>
        Dispatcher.UIThread.Post(() => ServerRoundTrip = roundTrip);

    // Raised on socket and thread pool threads
    private void OnCurrentUserChanged(UserModel user) => Dispatcher.UIThread.Post(() =>
    {
        User = user;
        UserCard.AttachUser(user);
    });

    // Raised on the transport thread
    private void OnPermissionsChanged(CoordinatorPermissions permissions) =>
        Dispatcher.UIThread.Post(() => Access.Update(permissions));

    private void OnRequestRejected(RequestRejectedException exception)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            // The reconnect attempts are already shown in the status line
            if (_coordinatorSession?.ConnectionStatus == CoordinatorState.Reconnecting)
                return;

            if (_isRejectionMessageShown || DateTime.UtcNow < _nextRejectionMessageTime)
                return;

            _isRejectionMessageShown = true;

            try
            {
                await ShowMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.Ok,
                    RequestRejectionMessages.Get(exception.Reason), RequestRejectionMessages.Title));
            }
            finally
            {
                _isRejectionMessageShown = false;
                _nextRejectionMessageTime = DateTime.UtcNow + RejectionMessagePause;
            }
        });
    }

    /// <summary>
    /// The coordinator session reconnects on its own. Until it is back the hybrid session is
    /// closed, since its media transport does not survive a lost connection; opened chats stay
    /// and catch up once the connection returns.
    /// </summary>
    /// <param name="isSuspended">Closed on purpose while the application is off the screen, which is no news to tell.</param>
    private void OnConnectionLost(bool isSuspended)
    {
        var wasInHybridSession = OpenedSessions?.HybridSession != null;

        // Before anything is closed here: what is remembered is where the user was, not this
        _lastSession.IsKept = true;
        _restoreId++;

        if (CurrentContent is not ActiveChatSessionVM)
            ChangeContent(null);
        OpenedSessions?.LeaveHybridSession();
        ServerTitle.DetachCoordinator();
        SessionsList.ShowReconnecting();
        DetachUser();

        if (!isSuspended)
            _ = SetStatus(wasInHybridSession ? "main.status.connectionLostVoiceClosed" : "main.status.connectionLost", true);
    }

    private void OnReconnectScheduled(int attempt, TimeSpan delay)
    {
        // The first attempt follows at once, the lost connection message stays for it
        if (attempt == 1)
            return;

        _ = StatusLine.SetAsync(Strings.Format("main.status.reconnectAttempt", ("attempt", attempt)), true);
    }

    private void DetachUser()
    {
        IsConnected = false;
        User = null;
        UserCard.DetachUser();
    }

    private async Task DisconnectFromCoordinator(string? disconnectMessage = null, Icon messageIcon = Icon.Info)
    {
        // Already left, e.g. by the user while a failed load was still telling about it
        if (_coordinatorSession is not { } session)
            return;

        _restoreId++;
        UnsubscribeFromCoordinator();
        ChangeContent(null);
        DetachSessionManager();

        // Left for good, by the user or by an end no reconnect follows: there is nowhere to return to
        _lastSession.Forget(session.CoordinatorModel?.Id);

        // Before the socket is closed: until then the menu would still offer to disconnect
        DetachUser();
        await session.Disconnect();
        await SetStatus("main.status.offline", false);
        if (!string.IsNullOrEmpty(disconnectMessage))
            Dispatcher.UIThread.Post(async () =>
            {
                await ShowMessageBox.Handle(new MessageBoxVM(messageIcon,
                    Buttons.Ok, Strings.Format("main.disconnected.message", ("reason", disconnectMessage)),
                    Strings.Get("main.disconnected.title")));
            });
    }

    private async Task AttachSessionManager(ICoordinatorSession session, int restoreId)
    {
        try
        {
            await SessionsList.AttachCoordinator(session);
            if (!IsRestoreCurrent(session, restoreId))
                return;

            await ServerTitle.AttachCoordinator(session);
            AttachOpenedSessions(session);
            await RestoreLastSessionAsync();
            if (IsRestoreCurrent(session, restoreId))
                await SetStatus("main.status.connected", false);
        }
        catch (Exception e)
        {
            // Failed because the connection went away: the reconnect or the disconnect that follows tells about it
            if (!IsRestoreCurrent(session, restoreId))
            {
                LogSessionsLoadInterrupted(e, session.GetCoordinatorName());
                return;
            }

            LogSessionsInfoFailed(e, session.GetCoordinatorName());
            await ShowMessageBox.Handle(new MessageBoxVM(Icon.Fatal, Buttons.Ok,
                Strings.Get("main.sessionsInfoFailed"), Strings.Get("common.error")));

            // The user may have disconnected while the message was open
            if (IsRestoreCurrent(session, restoreId))
                await DisconnectFromCoordinator();
        }
    }

    private void DetachSessionManager()
    {
        DetachOpenedSessions();
        SessionsList.DetachCoordinator();
        ServerTitle.DetachCoordinator();
    }

    private void AttachOpenedSessions(ICoordinatorSession session)
    {
        // Kept through a reconnect: the same coordinator session is connected again
        if (OpenedSessions == null)
        {
            var sessions = new OpenedSessions(session, _chatServices, ChatReads, CreateHybridSession,
                _users.ViewProfileCommand);
            sessions.SessionClosed += OnSessionClosed;
            sessions.HybridSessionChanged += OnHybridSessionChanged;
            OpenedSessions = sessions;
        }

        // On the first connect nothing is open yet, but chats cached by an earlier run may be gone
        RestoreOpenedChats(OpenedSessions);
    }

    private void RestoreOpenedChats(OpenedSessions sessions)
    {
        if (!SessionsList.IsEmpty && !SessionsList.HasSessions)
            return;

        sessions.CloseChatsExcept(SessionsList.Sessions
            .Where(session => session is not HybridSessionVM)
            .Select(session => session.Model.Id)
            .ToHashSet());

        // The list was loaded anew and does not know which of its sessions is on screen
        if (CurrentContent is ActiveChatSessionVM chat)
            SessionsList.SetShownSession(chat.Id, false);
    }

    private void DetachOpenedSessions()
    {
        var sessions = OpenedSessions;
        if (sessions == null)
            return;

        OpenedSessions = null;
        // Disposed while still subscribed, so the sessions list learns that we left the hybrid session
        sessions.Dispose();
        sessions.SessionClosed -= OnSessionClosed;
        sessions.HybridSessionChanged -= OnHybridSessionChanged;
    }

    /// <summary>
    /// Tells the connection that the application left the screen: after a short while it closes, so
    /// that the server counts the user as away and notifies the device instead. A hybrid session
    /// keeps the connection: its media would not survive.
    /// </summary>
    public Task SuspendConnection() =>
        _coordinatorSession == null || OpenedSessions?.IsInHybridSession == true
            ? Task.CompletedTask
            : _coordinatorSession.Suspend();

    /// <summary>
    /// Tells the connection that the application is back, which restores it if it was closed.
    /// </summary>
    public Task ResumeConnection() => _coordinatorSession?.Resume() ?? Task.CompletedTask;

    // Raised on socket and thread pool threads
    private void OnConnectionStateChanged(CoordinatorState status)
    {
        switch (status)
        {
            case CoordinatorState.Connecting:
                _ = SetStatus("main.status.waitingForServer", true);
                return;
            // Only a reconnect that stopped on an unexpected error reports it here
            case CoordinatorState.Failed:
                PostDisconnect(Strings.Get("main.reconnect.failed"));
                return;
            // Only a reconnect reports it here, after the server rejected the credentials
            case CoordinatorState.AuthenticationFailed:
                PostDisconnect(Strings.Get("main.reconnect.loginRejected"));
                return;
            // Only a reconnect reports it here: banned while online, the user is disconnected and comes back
            case CoordinatorState.Denied:
                PostDisconnect(Strings.Get("main.reconnect.banned"));
                return;
            // Only a reconnect reports it here: the server was updated or rolled back while the user was online
            case CoordinatorState.ClientOutdated:
                PostDisconnect(Strings.Get("main.reconnect.clientOutdated"));
                return;
            case CoordinatorState.ServerOutdated:
                PostDisconnect(Strings.Get("main.reconnect.serverOutdated"));
                return;
            // Only a reconnect reports it here
            case CoordinatorState.CoordinatorMismatch:
                PostDisconnect(CertificateTrustMessages.GetCoordinatorMismatch(_coordinatorSession?.CoordinatorMismatch),
                    Icon.KeyWarning);
                return;
            // Only a reconnect reports it here; trusting a new certificate needs the connect window
            case CoordinatorState.CertificateNotTrusted:
                PostDisconnect(CertificateTrustMessages.GetReconnectStopped(_coordinatorSession?.UntrustedCertificate),
                    Icon.KeyWarning);
                return;
            case CoordinatorState.Disconnected:
                PostDisconnect(Strings.Get("main.serverShutDown"));
                return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            switch (status)
            {
                case CoordinatorState.Reconnecting:
                    OnConnectionLost(isSuspended: false);
                    break;
                case CoordinatorState.Suspended:
                    OnConnectionLost(isSuspended: true);
                    break;
                case CoordinatorState.Connected:
                    _ = RestoreCoordinatorData();
                    break;
            }
        });

        // The message is read here, on the thread of the state change, while it still belongs to it
        void PostDisconnect(string message, Icon icon = Icon.Info) =>
            Dispatcher.UIThread.Post(() => _ = DisconnectFromCoordinator(message, icon));
    }
}
