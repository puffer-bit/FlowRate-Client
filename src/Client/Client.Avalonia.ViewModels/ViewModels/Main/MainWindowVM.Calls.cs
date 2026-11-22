using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Avalonia.Localization;
using Client.Core.Coordinator;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;
using Client.ViewModels.Sessions.Chat;
using Client.Services;
using Client.ViewModels.Notifications;
using Client.ViewModels.Sessions.Hybrid;
using Shared.Enums;
using Shared.Models.DataTransferObjects;

namespace Client.ViewModels.Main;

public partial class MainWindowVM
{
    private readonly Client.Application.Sounds.ISoundService? _callSounds;
    public event Action<Client.Core.Push.PushPayload>? CallNotificationReceived;
    public bool UsesSystemIncomingCallSound { get; set; }

    private ToastVM? _callToast;
    private Guid? _shownCall;
    private DateTimeOffset _shownCallExpiresAt;
    private IDisposable? _callRinging;

    private bool IsRingingFor(ChatCallDTO call) => call.State == ChatCallState.Active
        && _coordinatorSession?.GetCurrentUser().Id is { } me
        && call.Participants.Any(p => p.UserId == me && p.State == ChatCallParticipantState.Ringing);

    /// <summary>Tells the platform about a ring that started or stopped, for its own call notification.</summary>
    private void ReportCallNotification(ChatCallDTO call, string chatName, string callerName, bool isRinging)
    {
        if (_coordinatorSession?.GetCurrentUser().Id is not { } me || ConnectedCoordinatorId is not { } coordinatorId)
            return;
        CallNotificationReceived?.Invoke(new Client.Core.Push.PushPayload(coordinatorId, call.ChatId, chatName, 0,
            call.CallerId, callerName, null, Client.Core.Push.PushAttachmentKind.None,
            call.CreatedAt.ToUnixTimeMilliseconds(), CallId: call.CallId, CallState: isRinging ? 0 : 2,
            ExpiresAt: _shownCallExpiresAt.ToUnixTimeMilliseconds(), RecipientId: me));
    }

    public bool IsCallAccountOnScreen(string coordinatorId, int userId) => IsWindowActive
        && _coordinatorSession is { ConnectionStatus: CoordinatorState.Connected } session
        && session.CoordinatorModel?.Id == coordinatorId && session.GetCurrentUser().Id == userId;

    public void RefreshCallRinging()
    {
        _callRinging?.Dispose();
        _callRinging = null;
        if (_notifications.IsSoundMuted || _shownCall is not { } shown || _coordinatorSession == null
            || _coordinatorSession.ChatCalls.Values.FirstOrDefault(c => c.CallId == shown) is not { } call
            || !IsRingingFor(call)) return;
        if (UsesSystemIncomingCallSound && !IsWindowActive)
        {
            _ = ReportIncomingCallNotificationAsync(_coordinatorSession, call);
            return;
        }
        _callRinging = _callSounds?.PlayLoop(Client.Application.Sounds.SoundEvent.CallIncoming);
    }

    private void DismissCallToast()
    {
        _callRinging?.Dispose();
        _callRinging = null;
        if (_callToast != null) _ = _callToast.CloseAsync();
        _callToast = null;
        _shownCall = null;
    }

    private void OnChatCallChanged(ChatCallDTO call)
    {
        var coordinator = _coordinatorSession;
        Dispatcher.UIThread.Post(async () =>
        {
            if (coordinator == null || coordinator != _coordinatorSession
                || coordinator.ConnectionStatus != CoordinatorState.Connected) return;
            // Notifications may have moved on while this one waited for the UI thread
            var latest = coordinator.ChatCalls.GetValueOrDefault(call.ChatId);
            var current = latest?.CallId == call.CallId ? latest : call.State == ChatCallState.Ended ? call : null;
            if (current == null) return;

            await ShowRingAsync(coordinator, current);
            await FollowCallSessionAsync(coordinator, current);
        });
    }

    private async Task ShowRingAsync(Client.Application.Coordinator.ICoordinatorSession coordinator, ChatCallDTO call)
    {
        if (!IsRingingFor(call))
        {
            if (_shownCall != call.CallId) return;
            DismissCallToast();
            var (chatName, callerName) = await CallPresentation.GetNamesAsync(coordinator, call, SessionsList.FindChat(call.ChatId));
            ReportCallNotification(call, chatName, callerName, false);
            return;
        }
        if (_shownCall == call.CallId) return;
        DismissCallToast();
        _shownCall = call.CallId;
        _shownCallExpiresAt = DateTimeOffset.UtcNow.AddSeconds(45);
        var chat = SessionsList.FindChat(call.ChatId);
        var (name, caller) = await CallPresentation.GetNamesAsync(coordinator, call, chat);
        if (coordinator != _coordinatorSession || _shownCall != call.CallId
            || coordinator.ChatCalls.GetValueOrDefault(call.ChatId) is not { } still || !IsRingingFor(still)) return;
        ReportCallNotification(call, name, caller, true);
        RefreshCallRinging();
        var isGroup = chat is { Kind: ChatKind.Group };
        _callToast = _notifications.ShowActions(Strings.Get(isGroup ? "calls.incomingGroup" : "calls.incoming"),
            isGroup ? Strings.Format("calls.incomingGroupText", ("user", caller), ("group", name)) : name,
            Strings.Get("calls.accept"), () => ChangeCallAsync(call, ChatCallAction.Join),
            Strings.Get("calls.decline"), () => ChangeCallAsync(call, ChatCallAction.Decline),
            primaryIconKey: "ChatCallStartedIcon", secondaryIconKey: "ChatCallEndedIcon");
    }

    /// <summary>Opens the media session of the call this device joined, keeps it current, closes it when out.</summary>
    private async Task FollowCallSessionAsync(Client.Application.Coordinator.ICoordinatorSession coordinator, ChatCallDTO call)
    {
        var opened = OpenedSessions?.HybridSession;
        if (opened?.Model.CallId == call.CallId)
        {
            if (call.State == ChatCallState.Ended || !call.IsSelectedDevice)
                OpenedSessions!.LeaveHybridSession();
            else
                opened.ApplyCall(call, SessionsList.FindChat(call.ChatId));
            return;
        }
        if (call.State != ChatCallState.Active || !call.IsSelectedDevice || call.Session is not { } session) return;
        if (!Client.Core.Media.MediaRuntime.IsEnabled)
        {
            _ = coordinator.ChangeChatCall(call.ChatId, call.CallId, ChatCallAction.Leave);
            return;
        }
        var chat = SessionsList.FindChat(call.ChatId);
        // The server names a direct chat by its members' ids: the call is named after the other member
        var (name, _) = await CallPresentation.GetNamesAsync(coordinator, call, chat);
        if (coordinator != _coordinatorSession || OpenedSessions?.HybridSession?.Model.CallId == call.CallId
            || coordinator.ChatCalls.GetValueOrDefault(call.ChatId) is not { State: ChatCallState.Active, IsSelectedDevice: true } still
            || still.CallId != call.CallId) return;
        // Entering the call brings it on screen, as joining a voice channel does
        var entered = OpenedSessions?.Open(new HybridSessionModel
        {
            Id = session.Id, Name = name,
            Capacity = session.Capacity, SessionType = SessionType.Hybrid, EngineType = session.EngineType,
            IsApprovalRequested = false, CallId = call.CallId, CallStartedAt = call.StartedAt,
            CallOwnerUserId = chat is { Kind: ChatKind.Group } ? chat.OwnerUserId : null
        });
        if (entered != null) ChangeContent(entered);
    }

    /// <summary>Brings the call session we are in on screen.</summary>
    private void ShowCallSession()
    {
        if (OpenedSessions?.HybridSession is { Model.CallId: not null } call) ChangeContent(call);
    }

    /// <summary>Connects the call session to the panel of peers and the state of the call.</summary>
    private async Task AttachCallSessionAsync(ActiveHybridSessionVM? session)
    {
        if (session?.Model.CallId is not { } callId)
        {
            await SessionsList.SetCallSessionAsync(null);
            if (session != null) session.PeerList = SessionsList.FindHybridSession(session.Model.Id);
            return;
        }
        session.RemoveFromGroup = SessionsList.RemoveFromGroupAsync;
        var setting = SessionsList.SetCallSessionAsync(session.Model);
        session.PeerList = SessionsList.CallSession;
        if (_coordinatorSession?.ChatCalls.GetValueOrDefault(session.Model.Id) is { } call && call.CallId == callId)
            session.ApplyCall(call, SessionsList.FindChat(call.ChatId));
        await setting;
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
        await session.ChangeChatCall(chatId, callId, accept ? ChatCallAction.Join : ChatCallAction.Decline);
    }

    private async Task ReportIncomingCallNotificationAsync(Client.Application.Coordinator.ICoordinatorSession coordinator,
        ChatCallDTO call)
    {
        var (name, caller) = await CallPresentation.GetNamesAsync(coordinator, call, SessionsList.FindChat(call.ChatId));
        if (coordinator != _coordinatorSession || _shownCall != call.CallId
            || coordinator.ChatCalls.GetValueOrDefault(call.ChatId) is not { } still || !IsRingingFor(still)) return;
        ReportCallNotification(call, name, caller, true);
    }

    private void OnChatCallFailed(ErrorCode error) => Dispatcher.UIThread.Post(() =>
        _notifications.Show(NotificationType.Warning, Strings.Get(error switch
        {
            ErrorCode.CallBusy => "calls.busy",
            ErrorCode.CallAnsweredElsewhere => "calls.answeredElsewhere",
            ErrorCode.CallNotFound => "calls.ended",
            ErrorCode.NotConnected => "calls.offline",
            ErrorCode.SessionFull => "calls.full",
            _ => "calls.failed"
        })));

    private async Task ChangeCallAsync(ChatCallDTO call, ChatCallAction action)
    {
        if (_coordinatorSession == null) return;
        if (action == ChatCallAction.Join && !Client.Core.Media.MediaRuntime.IsEnabled)
        {
            _notifications.Show(NotificationType.Warning, Strings.Get("calls.mediaDisabled"));
            return;
        }
        await _coordinatorSession.ChangeChatCall(call.ChatId, call.CallId, action);
    }
}
