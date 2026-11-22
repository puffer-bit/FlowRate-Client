using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Core.Coordinator;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;
using Client.Core.Users;
using ReactiveUI;
using Shared.Enums;
using Shared.Models.Media;

using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.ViewModels.Sessions.List;

/// <summary>
/// The peers of the voice channels and calls: loading them, the notifications about them and moving them.
/// </summary>
public partial class SessionsListVM
{
    /// <summary>Moves a peer of one hybrid session into another; dragging peers is off without it.</summary>
    public ReactiveCommand<(int SessionId, int UserId, int TargetSessionId), RxVoid>? MovePeerCommand { get; set; }

    /// <summary>The peer may be picked up and dragged into another hybrid session.</summary>
    public bool CanDragPeer(PeerVM peer) => MovePeerCommand != null && !IsStale && peer is { CanMove: true, IsLeaving: false };

    /// <summary>
    /// Whether the peer may be moved from one session into the other, as far as the list knows: the target must
    /// be another session the current user may move peers into and the moved user may see.
    /// </summary>
    public bool CanMovePeer(HybridSessionVM source, PeerVM peer, HybridSessionVM target)
    {
        if (target == source || !target.CanView || target.Model is not HybridSessionModel
            || target.Peers.Any(other => other.PeerData.UserId == peer.PeerData.UserId && !other.IsLeaving))
            return false;

        var permissions = Access.Permissions;
        return permissions.HasIn(target.Model, Permission.ViewSessions | Permission.MoveMembers)
               && permissions.InSession(target.Model, peer.UserData?.RoleIds ?? []).HasFlag(Permission.ViewSessions);
    }

    public void MovePeer(HybridSessionVM source, PeerVM peer, HybridSessionVM target)
    {
        if (MovePeerCommand != null && CanMovePeer(source, peer, target))
            MovePeerCommand.Execute((source.Model.Id, peer.PeerData.UserId, target.Model.Id)).Subscribe();
    }

    // Only one peer can be selected across all hybrid sessions
    private void ClearPeerSelectionExcept(HybridSessionVM selectedIn)
    {
        foreach (var session in HybridSessions)
        {
            if (session != selectedIn)
                session.SelectedPeer = null;
        }
    }

    /// <summary>
    /// Loads the peers of a voice channel or a call, then their volumes and users. <paramref name="isCurrent"/>
    /// tells whether the session still wants them once the peers have come.
    /// </summary>
    private async Task<RequestResult> LoadPeersAsync(ICoordinatorSession coordinator, HybridSessionVM session,
        Func<bool>? isCurrent = null)
    {
        var result = await coordinator.GetAllHybridSessionPeers(session.Model.Id);
        if (!result.Result.IsSuccess || isCurrent?.Invoke() == false)
            return result.Result;

        var peers = result.Value!.Values;
        foreach (var peer in peers)
            session.AddPeerToSession(peer);

        var userIds = peers.Select(peer => peer.UserId).ToList();
        // The peers show the defaults until then and get the stored settings through Changed
        _ = _playbackService.LoadAsync(coordinator.GetCurrentUser().CoordinatorInstanceId, userIds);
        foreach (var (userId, user) in await coordinator.GetUsers(userIds))
            session.UpdateUserModel(userId, user);
        return result.Result;
    }

    /// <summary>
    /// Applies a coordinator notification to a hybrid session of the list on the UI thread, behind
    /// the notifications before it: the join of a peer comes before its leave and its state.
    /// </summary>
    private void PostToHybrid(int sessionId, Action<HybridSessionVM> apply) => Dispatcher.UIThread.Post(() =>
    {
        if (FindHybridSession(sessionId) is { } session)
            apply(session);
    });

    private void AddPeerToSession(PeerJoinedEvent notification)
    {
        var coordinatorSession = _coordinatorSession!;
        var peer = notification.Peer;

        Dispatcher.UIThread.Post(async () =>
        {
            if (FindHybridSession(notification.SessionId) is not { } session)
                return;

            // Not awaited before adding: the leave and state changes of this peer are posted right behind
            session.AddPeerToSession(peer);
            _ = _playbackService.LoadAsync(coordinatorSession.GetCurrentUser().CoordinatorInstanceId, [peer.UserId]);
            session.UpdateUserModel(peer.UserId, await coordinatorSession.GetUser(peer.UserId));
        });
    }

    private void OnUserUpdated(UserModel user) => Dispatcher.UIThread.Post(() =>
    {
        foreach (var session in HybridSessions)
            session.UpdateUserModel(user.Id, user);
        foreach (var chat in Sessions.OfType<ChatSessionVM>())
            if (chat.Model is ChatSessionModel { Kind: ChatKind.Direct } model && model.MemberIds.Contains(user.Id)
                && user.Id != _coordinatorSession?.GetCurrentUser().Id)
                ApplyDirectPeer(chat, user);
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
            foreach (var session in HybridSessions)
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
            // Stopping the voice leaves no microphone producer.
            case PeerTrackStoppedEvent { SourceType: MediaSourceType.Microphone } stopped:
                UpdatePeerMicrophone(new PeerMicrophoneMuteChangedEvent(stopped.SessionId, stopped.UserId, true));
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
}
