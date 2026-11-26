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
using Client.ViewModels.Connections;
using Client.ViewModels.Dialogs;
using Client.ViewModels.Sessions;
using Client.ViewModels.Sessions.Chat;
using Client.ViewModels.Sessions.Hybrid;
using Client.ViewModels.Sessions.List;
using Shared.Enums;

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
    private DateTimeOffset _nextRejectionMessageTime;

    private Task SetStatus(string key, bool isActive) => StatusLine.SetAsync(() => Strings.Get(key), isActive);

    private async Task AutoConnectToCoordinator(SavedCoordinatorModel coordinator, SavedUserModel user)
    {
        string Connecting() => Strings.Format("main.status.connecting", ("host", coordinator.Address),
            ("port", coordinator.Port), ("user", user.Username));

        // An account without a device here logs in again: the connect window opens with its name filled in
        if (LoginPlan.For(user) is not { } plan)
        {
            await ConnectToCoordinator();
            return;
        }

        await StatusLine.SetAsync(Connecting, true);
        var session = _coordinatorFactory.CreateCoordinatorSession();
        LoginPromptVM.Attach(session, AskLogin);
        if (!await ConnectWithRetriesAsync(session, coordinator, user, plan))
            return;

        if (session.ConnectionStatus == CoordinatorState.CertificateNotTrusted)
        {
            if (!await CertificateTrustMessages.ConfirmTrustAsync(session, ShowMessageBox))
            {
                await SetStatus("main.status.offline", false);
                session.Dispose();
                return;
            }

            await StatusLine.SetAsync(Connecting, true);
            if (!await ConnectWithRetriesAsync(session, coordinator, user, plan))
                return;
        }

        // The device ended while the app was closed: the user logs in again
        if (session.ConnectionStatus == CoordinatorState.DeviceEnded)
        {
            session.Dispose();
            // The saved account is marked before the connect window lists it
            await _accounting.ForgetDeviceAsync(coordinator.CoordinatorId, user.Id);
            await SetStatus("main.status.offline", false);
            await ShowMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.Ok,
                Strings.Get("main.deviceEnded.notFound"), Strings.Get("main.deviceEnded.title")));
            await ConnectToCoordinator();
            return;
        }

        if (session.ConnectionStatus != CoordinatorState.Connected)
        {
            await SetStatus("main.status.connectionFailed", false);
            // Given up by the user: there is nothing to tell them
            if (session.ConnectionStatus != CoordinatorState.AuthenticationNeeded)
                await ShowMessageBox.Handle(session.ConnectionStatus == CoordinatorState.CoordinatorMismatch
                    ? CertificateTrustMessages.CreateMismatchMessage(session.CoordinatorMismatch)
                    : new MessageBoxVM(Icon.Error, Buttons.Ok,
                        Strings.Get(session.ConnectionStatus == CoordinatorState.PasskeyRequired
                            ? "connect.failed.passkeyRequired"
                            : "main.autoConnect.failed"), Strings.Get("main.autoConnect.failedTitle")));
            session.Dispose();
            return;
        }

        try
        {
            // An account of the earlier protocol has just moved over: it has a device and a new recovery phrase now
            var credentials = session.GetCurrentUserCredentials();
            var newPhrase = session.TakeNewRecoveryPhrase();
            await _accounting.SaveCoordinatorInstanceAsync(coordinator, new SavedUserModel
            {
                CoordinatorId = coordinator.CoordinatorId,
                Id = credentials.Id,
                Username = credentials.Username,
                Password = user.Password,
                DeviceId = credentials.DeviceId,
                DeviceKey = credentials.DeviceKey,
                RecoveryPhrase = newPhrase,
                IsRecoveryPhraseSaved = newPhrase == null && user.IsRecoveryPhraseSaved
            });
        }
        catch (Exception exception)
        {
            LogSuccessfulConnectionSaveFailed(exception, coordinator.CoordinatorId);
        }

        _coordinatorSession = session;
        await FinalizeCoordinatorConnection();
    }

    // The network of a phone that has just woken up or moved to another one is often not there yet when
    // the application starts, and a desktop may start before its network too: a failure the network can
    // explain is tried again a few times before it is told
    private static readonly TimeSpan[] AutoConnectRetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8)
    ];

    private static bool CanRetryAutoConnect(CoordinatorState state) => state is CoordinatorState.Failed
        or CoordinatorState.TimedOut or CoordinatorState.Disconnected or CoordinatorState.InternalServerError
        or CoordinatorState.HeartbeatTimedOut or CoordinatorState.HeartbeatServerShutdown;

    /// <returns>
    /// False when another connection took the place of this one meanwhile, made by the user from the
    /// connect window: the session is disposed and there is nothing to tell.
    /// </returns>
    private async Task<bool> ConnectWithRetriesAsync(ICoordinatorSession session, SavedCoordinatorModel coordinator,
        SavedUserModel user, LoginPlan plan)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await session.Connect(coordinator.Address, coordinator.Port, user.Password, plan,
                    coordinator.CoordinatorId);
            }
            catch (Exception exception)
            {
                // The state of the session is Failed then, which is retried or told below
                LogAutoConnectAttemptFailed(exception, coordinator.CoordinatorId, attempt);
            }

            if (_coordinatorSession != null)
            {
                session.Dispose();
                return false;
            }

            if (!CanRetryAutoConnect(session.ConnectionStatus) || attempt > AutoConnectRetryDelays.Length)
                return true;

            var delay = AutoConnectRetryDelays[attempt - 1];
            LogAutoConnectRetry(coordinator.CoordinatorId, attempt, session.ConnectionStatus, delay);
            var next = attempt + 1;
            await StatusLine.SetAsync(() => Strings.Format("main.status.autoConnectAttempt",
                ("host", coordinator.Address), ("port", coordinator.Port), ("attempt", next)), true);
            await Task.Delay(delay);

            if (_coordinatorSession != null)
            {
                session.Dispose();
                return false;
            }
        }
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
        _deviceEnded = false;
        var session = _coordinatorSession!;
        LoginPromptVM.Attach(session, AskLogin);
        _coordinatorSubscriptions
            // Calls in chats are hybrid sessions too
            .Add(() => _autoIdle?.Attach(session, () => OpenedSessions?.IsInHybridSession == true),
                () => _autoIdle?.Detach())
            .Add(() => session.ChatCallFailed += OnChatCallFailed, () => session.ChatCallFailed -= OnChatCallFailed)
            .Add(() => session.ChatCallChanged += OnChatCallChanged,
                () => { session.ChatCallChanged -= OnChatCallChanged; DismissCallToast(); })
            .Add(() => session.StateChanged += OnConnectionStateChanged, () => session.StateChanged -= OnConnectionStateChanged)
            .Add(() => session.ReconnectScheduled += OnReconnectScheduled, () => session.ReconnectScheduled -= OnReconnectScheduled)
            .Add(() => session.RequestRejected += OnRequestRejected, () => session.RequestRejected -= OnRequestRejected)
            .Add(() => session.DeviceEnded += OnDeviceEnded, () => session.DeviceEnded -= OnDeviceEnded)
            .Add(() => session.DeviceAdded += OnDeviceAdded, () => session.DeviceAdded -= OnDeviceAdded)
            .Add(() => session.ServerRestart += OnServerRestart, () => session.ServerRestart -= OnServerRestart)
            .Add(() => session.ServerRestartCancelled += OnServerRestartCancelled,
                () => session.ServerRestartCancelled -= OnServerRestartCancelled)
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
        Dispatcher.UIThread.Post(() => _ = PromptRecoveryPhraseAsync());
    }

    private void UnsubscribeFromCoordinator() => _coordinatorSubscriptions.Clear();

    /// <summary>The saved account of the session: its server and user, unless the device is temporary.</summary>
    private (string CoordinatorId, int UserId)? SavedAccountOf(ICoordinatorSession? session)
    {
        if (session is not { CoordinatorModel.Id: var coordinatorId } || session.IsTemporaryDevice)
            return null;
        try
        {
            return (coordinatorId, session.GetCurrentUser().Id);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private AccountSecurityHooks CreateSecurityHooks() => new(
        async phrase =>
        {
            if (SavedAccountOf(_coordinatorSession) is (string coordinatorId, int userId))
                await _accounting.KeepRecoveryPhraseAsync(coordinatorId, userId, phrase);
        },
        async keep =>
        {
            if (SavedAccountOf(_coordinatorSession) is (string coordinatorId, int userId))
                await _accounting.ConfirmRecoveryPhraseSavedAsync(coordinatorId, userId, keep);
        },
        async () => SavedAccountOf(_coordinatorSession) is (string coordinatorId, int userId)
            ? await _accounting.GetRecoveryPhraseAsync(coordinatorId, userId)
            : null,
        async () =>
        {
            if (SavedAccountOf(_coordinatorSession) is (string coordinatorId, int userId))
                await _accounting.ForgetDeviceAsync(coordinatorId, userId);
            Dispatcher.UIThread.Post(() => _ = Disconnect());
        });

    /// <summary>
    /// Asks the user to save the recovery phrase of the account while it waits on this device: after the account was
    /// registered or moved over, at each start until it is saved.
    /// </summary>
    private async Task PromptRecoveryPhraseAsync()
    {
        if (SavedAccountOf(_coordinatorSession) is not (string coordinatorId, int userId))
            return;

        try
        {
            var saved = (await _accounting.GetAllSavedCoordinatorsAsync())
                .SelectMany(coordinator => coordinator.Users)
                .FirstOrDefault(user => user.CoordinatorId == coordinatorId && user.Id == userId);
            if (saved is not { IsRecoveryPhraseSaved: false, RecoveryPhrase: { } phrase })
                return;

            var box = new MessageBoxVM(Icon.KeyWarning, Buttons.OkCancel, Strings.Get("recoveryPhrase.save.text"),
                Strings.Get("recoveryPhrase.save.title"))
            {
                Detail = Profile.AccountSecurityVM.NumberWords(phrase),
                CopyText = phrase,
                CheckBoxText = Strings.Get("recoveryPhrase.keepOnDevice")
            };
            if (await ShowMessageBox.Handle(box) == Result.Ok)
                await _accounting.ConfirmRecoveryPhraseSavedAsync(coordinatorId, userId, box.IsChecked);
        }
        catch (Exception exception)
        {
            LogSuccessfulConnectionSaveFailed(exception, coordinatorId);
        }
    }

    private async Task RestoreCoordinatorData()
    {
        if (_coordinatorSession is not { } session)
            return;

        var restoreId = ++_restoreId;
        IsConnected = true;
        ConnectionInfo.Attach(session.ConnectedAddress, session.IsCertificateVerified);
        User = session.GetCurrentUser();
        Access.Update(session.Permissions);
        _avatars.Attach(session);
        UserCard.AttachUser(User);

        await SetStatus("main.status.retrievingSessions", true);
        if (!IsRestoreCurrent(session, restoreId))
            return;

        SessionsList.ShowLoading();
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
        if (Client.Tools.AppInfo.LaunchOptions.Current.PreventAutoConnect)
        {
            _lastSession.IsKept = false;
            return;
        }
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

    // The session disconnects on its own; the user is told why it does not come back
    // A warning stays until its time; going down now is told once, the status line shows the reconnect
    private void OnServerRestart(ServerRestartEvent restart)
    {
        var kind = restart.Reason switch
        {
            ServerRestartReason.Update => "update",
            ServerRestartReason.Shutdown => "shutdown",
            _ => "restart"
        };
        if (restart.At is { } at)
        {
            var left = at - DateTimeOffset.UtcNow;
            _notifications.Show(NotificationType.Warning,
                Strings.Format($"server.restart.warning.{kind}",
                    ("time", at.ToLocalTime().ToString("t", System.Globalization.CultureInfo.CurrentCulture))),
                restart.Message, left > TimeSpan.FromSeconds(10) ? left : TimeSpan.FromSeconds(10));
            return;
        }

        _notifications.Show(restart.Reason == ServerRestartReason.Shutdown ? NotificationType.Error : NotificationType.Warning,
            Strings.Get($"server.restart.now.{kind}"), restart.Message);
    }

    private void OnServerRestartCancelled() =>
        _notifications.Show(NotificationType.Info, Strings.Get("server.restart.cancelled"));

    // The version a coordinator had when last seen in this run: a reconnect to a newer one says so
    private (string Id, string Version)? _knownServerVersion;

    private void NotifyServerUpdate(ICoordinatorSession session)
    {
        if (session.CoordinatorModel is not { Id: { } id, ServerVersion: { Length: > 0 } version })
            return;
        if (_knownServerVersion is { } known && known.Id == id && known.Version != version)
            _notifications.Show(NotificationType.Info, Strings.Format("server.updated", ("version", version)));
        _knownServerVersion = (id, version);
    }

    // Raised on any thread; the session dropped the key, and the saved account needs a login
    private volatile bool _deviceEnded;

    private void OnDeviceEnded(Shared.Enums.Auth.DeviceEndReason? reason)
    {
        _deviceEnded = true;
        Dispatcher.UIThread.Post(async () =>
        {
            await SetStatus("main.status.offline", false);
            await ShowMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.Ok,
                Strings.Get(reason == null ? "main.deviceEnded.notFound" : "main.deviceEnded.revoked"),
                Strings.Get("main.deviceEnded.title")));
        });
    }

    /// <summary>
    /// A device was added to the account, maybe not by the user: they are asked, and "not me" removes it and offers a
    /// new recovery phrase, which the one who added it may know.
    /// </summary>
    private void OnDeviceAdded(AccountDevice device)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (_coordinatorSession is not { } session)
                return;

            var method = Strings.Get("devices.addedBy." + device.AddedBy);
            var answer = await ShowMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.YesNo,
                Strings.Format("main.deviceAdded.question", ("device", device.DeviceName), ("platform", device.Platform),
                    ("method", method), ("address", device.LastAddress ?? "?")),
                Strings.Get("main.deviceAdded.title")));
            if (answer != Result.No)
                return;

            var revoked = await session.RevokeAccountDevice(device.Id);
            await ShowMessageBox.Handle(new MessageBoxVM(revoked.IsSuccess ? Icon.Info : Icon.Error, Buttons.Ok,
                Strings.Get(revoked.IsSuccess ? "main.deviceAdded.removed" : "main.deviceAdded.removeFailed"),
                Strings.Get("main.deviceAdded.title")));
            // A new recovery phrase is made there: the one who added the device may know the old one
            await _users.ManageSecurityCommand.Execute();
        });
    }

    private void OnRequestRejected(RequestFailedException exception)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            // The reconnect attempts are already shown in the status line
            if (_coordinatorSession?.ConnectionStatus == CoordinatorState.Reconnecting)
                return;

            if (_isRejectionMessageShown || DateTimeOffset.UtcNow < _nextRejectionMessageTime)
                return;

            _isRejectionMessageShown = true;

            try
            {
                await ShowMessageBox.Handle(new MessageBoxVM(Icon.Warning, Buttons.Ok,
                    RequestRejectionMessages.Get(exception.Code), RequestRejectionMessages.Title));
            }
            finally
            {
                _isRejectionMessageShown = false;
                _nextRejectionMessageTime = DateTimeOffset.UtcNow + RejectionMessagePause;
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

        _ = StatusLine.SetAsync(() => Strings.Format("main.status.reconnectAttempt", ("attempt", attempt)), true);
    }

    private void DetachUser()
    {
        IsConnected = false;
        ConnectionInfo.Detach();
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
            // Not awaited: the checks of the opened chats change once the positions of the others come
            _ = _chatServices.Receipts?.AttachAsync(session);

            await ServerTitle.AttachCoordinator(session);
            NotifyServerUpdate(session);
            AttachOpenedSessions(session);
            if (session.CurrentCall == null)
                await RestoreLastSessionAsync();
            // A push action may complete while the initial session list is still loading; rings shown then
            // waited for the chats to be named
            foreach (var call in session.ChatCalls.Values)
                OnChatCallChanged(call);
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
        _chatServices.Receipts?.Detach();
        ServerTitle.DetachCoordinator();
    }

    private void AttachOpenedSessions(ICoordinatorSession session)
    {
        // Kept through a reconnect: the same coordinator session is connected again
        if (OpenedSessions == null)
        {
            var sessions = new OpenedSessions(session, _chatServices, ChatReads, CreateHybridSession,
                _users.ViewProfileCommand, _users.BanUserCommand, _users.UnbanUserCommand, _users.OpenDirectChatCommand);
            sessions.RemoveFromGroup = SessionsList.RemoveFromGroupAsync;
            sessions.ShowCallSession = ShowCallSession;
            SessionsList.ShowCallSession = ShowCallSession;
            sessions.SessionClosed += OnSessionClosed;
            sessions.HybridSessionChanged += OnHybridSessionChanged;
            sessions.HybridSessionMovedOut += OnHybridSessionMovedOut;
            OpenedSessions = sessions;
        }

        // On the first connect nothing is open yet, but chats cached by an earlier run may be gone
        RestoreOpenedChats(OpenedSessions);
    }

    private void RestoreOpenedChats(OpenedSessions sessions)
    {
        if (SessionsList.State != Client.ViewModels.Sessions.List.SessionsListState.Loaded)
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
        sessions.HybridSessionMovedOut -= OnHybridSessionMovedOut;
    }

    /// <summary>
    /// Tells the connection that the application left the screen: after a short while it closes, so
    /// that the server counts the user as away and notifies the device instead. A hybrid session
    /// keeps the connection: its media would not survive.
    /// </summary>
    public Task SuspendConnection() =>
        _coordinatorSession == null || OpenedSessions?.IsInHybridSession == true
            || _coordinatorSession.CurrentCall != null
            ? Task.CompletedTask
            : _coordinatorSession.Suspend();

    /// <summary>
    /// Tells the connection that the application is back, which restores it if it was closed.
    /// </summary>
    public Task ResumeConnection() => _coordinatorSession?.Resume() ?? Task.CompletedTask;

    // Raised on socket and thread pool threads
    private void OnConnectionStateChanged(CoordinatorState status)
    {
        if (status != CoordinatorState.Connected) Dispatcher.UIThread.Post(DismissCallToast);
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
            case CoordinatorState.OnlineLimitReached:
                PostDisconnect(Strings.Get("connect.failed.onlineLimitReached"));
                return;
            case CoordinatorState.UserLimitReached:
                PostDisconnect(Strings.Get("connect.failed.userLimitReached"));
                return;
            case CoordinatorState.AuthenticationFailed:
                PostDisconnect(Strings.Get("main.reconnect.loginRejected"));
                return;
            // Only a reconnect reports it here: the user gave up the password or code it asked for
            case CoordinatorState.AuthenticationNeeded:
                PostDisconnect(Strings.Get("main.reconnect.loginGivenUp"));
                return;
            case CoordinatorState.PasskeyRequired:
                PostDisconnect(Strings.Get("main.reconnect.passkeyRequired"));
                return;
            // Told by OnDeviceEnded
            case CoordinatorState.DeviceEnded:
                return;
            // Only a reconnect reports it here: banned while online, the user is disconnected and comes back
            case CoordinatorState.Denied:
                PostDisconnect(Strings.Get("main.reconnect.banned") + (string.IsNullOrWhiteSpace(_coordinatorSession?.BanReason) ? "" : "\n" + _coordinatorSession.BanReason));
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
                if (_deviceEnded)
                {
                    Dispatcher.UIThread.Post(() => _ = DisconnectFromCoordinator());
                    return;
                }
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
