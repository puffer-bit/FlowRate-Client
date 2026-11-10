using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Avalonia.Localization;
using Client.Core.Coordinator;
using Client.Core.Sessions.Hybrid;
using Client.Services;
using Client.ViewModels.Notifications;
using Shared.Enums;
using Shared.Models.DataTransferObjects;

namespace Client.ViewModels.Main;

public partial class MainWindowVM
{
    private readonly Client.Application.Sounds.ISoundService? _callSounds;
    public event Action<Client.Core.Push.PushPayload>? CallNotificationReceived;
    public bool UsesSystemIncomingCallSound { get; set; }

    private void ReportCallNotification(DirectCallDTO call, string name)
    {
        if (_coordinatorSession?.GetCurrentUser().Id != call.CalleeId || ConnectedCoordinatorId is not { } coordinatorId) return;
        CallNotificationReceived?.Invoke(new Client.Core.Push.PushPayload(coordinatorId, call.ChatId, name, 0,
            call.CallerId, name, null, Client.Core.Push.PushAttachmentKind.None, call.CreatedAt.ToUnixTimeMilliseconds(),
            CallId: call.CallId, CallState: (int)call.State, ExpiresAt: call.CreatedAt.AddSeconds(45).ToUnixTimeMilliseconds(),
            RecipientId: call.CalleeId));
    }

    public bool IsCallAccountOnScreen(string coordinatorId, int userId) => IsWindowActive
        && _coordinatorSession is { ConnectionStatus: CoordinatorState.Connected } session
        && session.CoordinatorModel?.Id == coordinatorId && session.GetCurrentUser().Id == userId;
    private IDisposable? _callRinging;

    public void RefreshCallRinging()
    {
        _callRinging?.Dispose();
        _callRinging = null;
        if (_notifications.IsSoundMuted || _coordinatorSession?.DirectCall is not { State: DirectCallState.Ringing } call
            || _shownCall != call.CallId) return;
        if (_coordinatorSession.GetCurrentUser().Id != call.CalleeId) return;
        if (UsesSystemIncomingCallSound && !IsWindowActive)
        {
            ReportCallNotification(call, CallName(call));
            return;
        }
        _callRinging = _callSounds?.PlayLoop(Client.Application.Sounds.SoundEvent.CallIncoming);
    }

    private ToastVM? _callToast;
    private Guid? _shownCall;

    private void DismissCallToast()
    {
        _callRinging?.Dispose();
        _callRinging = null;
        if (_callToast != null) _ = _callToast.CloseAsync();
        _callToast = null;
        _shownCall = null;
    }

    private void OnDirectCallChanged(DirectCallDTO call)
    {
        var coordinator = _coordinatorSession;
        Dispatcher.UIThread.Post(async () =>
        {
            if (coordinator == null || coordinator != _coordinatorSession
                || coordinator.ConnectionStatus != CoordinatorState.Connected) return;
            if (call.State != DirectCallState.Ended
                && (coordinator.DirectCall?.CallId != call.CallId || coordinator.DirectCall.State != call.State)) return;
            if (call.State == DirectCallState.Ringing)
            {
                var incoming = coordinator.GetCurrentUser().Id == call.CalleeId;
                if (!incoming && !call.IsSelectedDevice) return;
                if (_shownCall == call.CallId) return;
                DismissCallToast();
                _shownCall = call.CallId;
                var peerId = incoming ? call.CallerId : call.CalleeId;
                var peer = await coordinator.GetUser(peerId);
                if (coordinator != _coordinatorSession || coordinator.DirectCall?.CallId != call.CallId
                    || coordinator.DirectCall.State != DirectCallState.Ringing) return;
                var name = peer?.DisplayName ?? peer?.Username ?? Strings.Get("calls.peer");
                ReportCallNotification(call, name);
                RefreshCallRinging();
                _callToast = _notifications.ShowActions(Strings.Get(incoming ? "calls.incoming" : "calls.outgoing"), name,
                    Strings.Get(incoming ? "calls.accept" : "calls.cancel"),
                    () => ChangeCallAsync(call, incoming ? DirectCallAction.Accept : DirectCallAction.End),
                    Strings.Get(incoming ? "calls.decline" : "calls.openChat"),
                    incoming ? () => ChangeCallAsync(call, DirectCallAction.Decline)
                        : () => { TryShowChat(ConnectedCoordinatorId!, call.ChatId); return Task.CompletedTask; });
                return;
            }
            ReportCallNotification(call, "");
            if (_shownCall == call.CallId) DismissCallToast();
            if (call.State == DirectCallState.Active && call.IsSelectedDevice && call.Session is { } session)
            {
                if (!Client.Core.Media.MediaRuntime.IsEnabled)
                {
                    _ = ChangeCallAsync(call, DirectCallAction.End);
                    return;
                }
                OpenedSessions?.Open(new HybridSessionModel
                {
                    Id = session.Id, Name = CallName(call), Capacity = 2, SessionType = SessionType.Hybrid,
                    EngineType = session.EngineType, HostId = session.HostId,
                    IsAudioRequested = true, IsApprovalRequested = false, DirectCallId = call.CallId,
                    DirectCallStartedAt = call.StartedAt
                });
            }
            else if (call.State == DirectCallState.Ended)
            {
                if (OpenedSessions?.HybridSession?.Model.DirectCallId == call.CallId)
                    OpenedSessions.LeaveHybridSession();
                _notifications.Show(NotificationType.Info, Strings.Get(call.EndReason switch
                {
                    DirectCallEndReason.Declined => "calls.declined",
                    DirectCallEndReason.Missed => "calls.missed",
                    DirectCallEndReason.Cancelled => "calls.cancelled",
                    DirectCallEndReason.Disconnected => "calls.disconnected",
                    _ => "calls.ended"
                }));
            }
        });
    }

    /// <summary>Reconnects the saved recipient account before submitting a notification action.</summary>
    public async Task HandlePushCallAsync(string coordinatorId, int userId, int chatId, Guid callId, bool accept)
    {
        if (_coordinatorSession?.CoordinatorModel?.Id != coordinatorId
            || _coordinatorSession.GetCurrentUser().Id != userId)
        {
            var saved = (await _accounting.GetAllSavedCoordinatorsAsync())
                .FirstOrDefault(server => server.CoordinatorId == coordinatorId);
            var user = saved?.Users.FirstOrDefault(account => account.Id == userId);
            if (saved == null || user == null) return;
            if (_coordinatorSession is { } previous)
            {
                await DisconnectFromCoordinator();
                previous.Dispose();
                if (_coordinatorSession == previous) _coordinatorSession = null;
            }
            await AutoConnectToCoordinator(saved, user);
        }
        else await ResumeConnection();
        if (_coordinatorSession is not { ConnectionStatus: CoordinatorState.Connected } session
            || session.CoordinatorModel?.Id != coordinatorId || session.GetCurrentUser().Id != userId) return;
        if (accept && !Client.Core.Media.MediaRuntime.IsEnabled)
        {
            _notifications.Show(NotificationType.Warning, Strings.Get("calls.mediaDisabled"));
            return;
        }
        await session.ChangeDirectCall(chatId, callId, accept ? DirectCallAction.Accept : DirectCallAction.Decline);
    }

    private string CallName(DirectCallDTO call)
    {
        var peer = SessionsList.Sessions.OfType<Client.ViewModels.Sessions.List.ChatSessionVM>()
            .FirstOrDefault(chat => chat.Model.Id == call.ChatId);
        return peer?.DisplayName ?? Strings.Get("calls.peer");
    }

    private void OnDirectCallFailed(ErrorCode error) => Dispatcher.UIThread.Post(() =>
        _notifications.Show(NotificationType.Warning, Strings.Get(error == ErrorCode.Conflict
            ? "calls.busy" : error == ErrorCode.NotConnected ? "calls.offline" : "calls.failed")));

    private async Task ChangeCallAsync(DirectCallDTO call, DirectCallAction action)
    {
        if (_coordinatorSession == null) return;
        if (action == DirectCallAction.Accept && !Client.Core.Media.MediaRuntime.IsEnabled)
        {
            _notifications.Show(NotificationType.Warning, Strings.Get("calls.mediaDisabled"));
            return;
        }
        await _coordinatorSession.ChangeDirectCall(call.ChatId, call.CallId, action);
    }
}
