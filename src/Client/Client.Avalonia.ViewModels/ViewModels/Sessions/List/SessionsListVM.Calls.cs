using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Core.Coordinator;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;
using ReactiveUI;
using Shared.Enums;
using Shared.Models.DataTransferObjects;

namespace Client.ViewModels.Sessions.List;

/// <summary>
/// Chat calls: the media session of a running call is kept under its chat, the one we are in is <see cref="CallSession"/>.
/// </summary>
public partial class SessionsListVM
{
    /// <summary>Brings the call session we are in on screen.</summary>
    public Action? ShowCallSession { get; set; }

    /// <summary>The call item of a chat's menu: starts the call, joins it, or shows it when we are in it.</summary>
    /// <summary>Starts or joins the call of a chat of the list, as its call button does.</summary>
    public Task CallChatAsync(int chatId) => CallAsync(chatId);

    private async Task CallAsync(int chatId)
    {
        if (_coordinatorSession is not { ConnectionStatus: CoordinatorState.Connected } coordinator
            || !Client.Core.Media.MediaRuntime.IsEnabled) return;
        var running = coordinator.ChatCalls.GetValueOrDefault(chatId);
        if (running is { IsSelectedDevice: true })
        {
            ShowCallSession?.Invoke();
            return;
        }
        // Starting while a call runs joins it on the server too; its ID makes the request plain
        await coordinator.ChangeChatCall(chatId, running?.CallId ?? Guid.NewGuid(),
            running != null ? ChatCallAction.Join : ChatCallAction.Start);
    }

    // Raised on the socket thread
    private void OnChatCallChanged(ChatCallDTO call) =>
        Dispatcher.UIThread.Post(FollowCalls);

    /// <summary>
    /// The media session of the chat call we are in. It is not a row of the list: the session panel shows its
    /// peers, with the same rows and menus as the voice channels. It is the one its chat shows under it.
    /// </summary>
    public HybridSessionVM? CallSession
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>Follows the peers of the media session of a chat call we entered, or of none.</summary>
    public Task SetCallSessionAsync(HybridSessionModel? model)
    {
        var chat = model != null ? FindSession<ChatSessionVM>(model.Id) : null;
        var call = chat != null ? FollowCall(chat, model!.CallId, model) : null;
        if (CallSession != call && CallSession != null) CallSession.IsInCall = false;
        CallSession = call;
        foreach (var other in Sessions.OfType<ChatSessionVM>())
            other.IsEntered = other == chat && call != null;
        if (call != null)
        {
            call.IsInCall = true;
            // Entered, the call unfolds under its chat as the voice channel we are in shows its peers
            chat!.IsCallExpanded = true;
        }
        UpdateFolding();
        return Task.CompletedTask;
    }

    /// <summary>Keeps under each chat the peers of its running call, from the calls the coordinator knows.</summary>
    private void FollowCalls()
    {
        var calls = _coordinatorSession?.ChatCalls;
        foreach (var chat in Sessions.OfType<ChatSessionVM>())
        {
            var call = calls?.GetValueOrDefault(chat.Model.Id);
            chat.HasActiveCall = call != null;
            FollowCall(chat, call is { State: ChatCallState.Active } ? call.CallId : null, null);
        }
    }

    /// <summary>
    /// The media session of the chat's call, made when the call is new to the list and loaded with its peers.
    /// The session of a call that ended goes; <paramref name="model"/> is the one we entered, when we did.
    /// </summary>
    private HybridSessionVM? FollowCall(ChatSessionVM chat, Guid? callId, HybridSessionModel? model)
    {
        if (chat.CallPeers is { Model: HybridSessionModel { CallId: { } known } } existing && known == callId)
        {
            if (model != null) existing.Model = model;
            return existing;
        }
        if (chat.CallPeers is { } ended)
        {
            if (CallSession == ended) CallSession = null;
            chat.CallPeers = null;
            chat.IsEntered = false;
        }
        if (callId == null || _coordinatorSession == null)
            return null;

        var chatModel = chat.Model as ChatSessionModel;
        var call = CreateHybridSessionVM(model ?? new HybridSessionModel
        {
            Id = chat.Model.Id, Name = chat.DisplayName, Capacity = 0, SessionType = SessionType.Hybrid,
            EngineType = Shared.Enums.WebRTC.WebRTCEngine.Mediasoup, IsApprovalRequested = false, CallId = callId,
            CallOwnerUserId = chatModel is { Kind: ChatKind.Group } ? chatModel.OwnerUserId : null
        });
        call.RemoveFromGroup = RemoveFromGroupAsync;
        chat.CallPeers = call;
        // A call that ended or was replaced meanwhile gets no peers
        _ = LoadPeersAsync(_coordinatorSession, call, () => chat.CallPeers == call);
        return call;
    }
}
