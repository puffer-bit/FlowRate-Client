using Shared.Enums;
using Shared.Models;
using Shared.Models.Notifications.CoordinatorInfo;
using Shared.Models.Notifications.Heartbeat;
using Shared.Models.Notifications.SessionInfo;
using Shared.Models.Media;
using Client.Core.Events;

namespace Client.Api.Mappers;

internal static class CoordinatorEventMapper
{
    /// <summary>
    /// Translates a message the coordinator sent on its own.
    /// </summary>
    /// <returns>The event, or null for a message the client does not react to yet.</returns>
    public static CoordinatorEvent? ToEvent(this BaseMessage message) => message switch
    {
        HeartbeatServerClosingNotification closing => new ServerClosingEvent(closing.Message),
        HeartbeatServerRestartingNotification restarting => new ServerRestartingEvent(restarting.Message),
        CoordinatorUserSessionEndedNotification ended => new SessionEndedEvent(ended.Reason),

        CoordinatorReconfiguredNotification reconfigured =>
            new CoordinatorReconfiguredEvent(reconfigured.Coordinator.ToModel()),
        CoordinatorUserUpdatedNotification updated => new UserUpdatedEvent(updated.User.ToModel()),
        // Brings the status of the user; the client knows of users only by their updates
        CoordinatorUserConnectedNotification connected => new UserUpdatedEvent(connected.User.ToModel()),
        CoordinatorUserDeletedNotification deleted => new UserDeletedEvent(deleted.UserId),
        CoordinatorOnlineUsersChangedNotification changed => new OnlineUsersChangedEvent(changed.ConnectedUsersCount),
        // CoordinatorPermissionsChangedNotification is left out: permissions follow from the roles every client has
        CoordinatorRolesChangedNotification changed => new RolesChangedEvent(changed.Roles.ToModels()),

        HybridSessionCreatedNotification created => new SessionAddedEvent(created.Session.ToModel()),
        HybridSessionReconfiguredNotification reconfigured => new SessionReconfiguredEvent(reconfigured.Session.ToModel()),
        HybridSessionDeletedNotification deleted => new SessionDeletedEvent(deleted.SessionId, SessionType.Hybrid),

        ChatSessionCreatedNotification created => new SessionAddedEvent(created.Session.ToModel()),
        ChatSessionReconfiguredNotification reconfigured => new SessionReconfiguredEvent(reconfigured.Session.ToModel()),
        ChatSessionDeletedNotification deleted => new SessionDeletedEvent(deleted.SessionId, SessionType.Chat),
        SessionLayoutChangedNotification changed => new SessionLayoutChangedEvent(changed.Layout.ToModel()),

        ChatSessionMessageAddedNotification added => new ChatMessageAddedEvent(added.ChatId, added.Message.ToModel()),
        ChatSessionMessageEditedNotification edited => new ChatMessageEditedEvent(edited.ChatId, edited.Message.ToModel()),
        ChatSessionMessageDeletedNotification deleted => new ChatMessageDeletedEvent(deleted.ChatId, deleted.MessageId, deleted.Revision),
        ChatReadStateChangedNotification readChanged => new ChatReadStateChangedEvent(readChanged.State.ToModel()),

        HybridSessionStateChangedNotification changed => new SessionNodeStateChangedEvent(changed.SessionId, changed.State),
        HybridSessionSFUNodeChangedNotification changed => new SessionNodeReplacedEvent(changed.SessionId),
        HybridSessionSFUNodeConnectionChangedNotification { IsConnected: true } connected =>
            new SessionNodeReplacedEvent(connected.SessionId),

        HybridSessionPeerJoinedNotification joined =>new PeerJoinedEvent(joined.SessionId, joined.Peer.ToModel()),
        HybridSessionPeerLeftNotification left => new PeerLeftEvent(left.SessionId, left.UserId),
        // Without an initiator and a reason the server deleted the session, which is announced on its own;
        // with a reason the server removed the peer itself
        HybridSessionPeerKickedNotification { InitiatorUserId: null, Reason: null } kicked =>
            new PeerLeftEvent(kicked.SessionId, kicked.UserId),
        HybridSessionPeerKickedNotification kicked =>
            new PeerKickedEvent(kicked.SessionId, kicked.UserId, kicked.InitiatorUserId, kicked.Reason),
        HybridSessionPeerRejectedNotification rejected =>
            new PeerRejectedEvent(rejected.SessionId, rejected.UserId, rejected.InitiatorUserId),
        HybridSessionPeerStateChangedNotification changed =>
            new PeerStateChangedEvent(changed.SessionId, changed.UserId, changed.State),

        HybridSessionPeerMicrophoneMuteChangedNotification changed =>
            new PeerMicrophoneMuteChangedEvent(changed.SessionId, changed.UserId, changed.IsMuted),
        HybridSessionPeerSoundMuteChangedNotification changed =>
            new PeerSoundMuteChangedEvent(changed.SessionId, changed.UserId, changed.IsMuted),
        HybridSessionPeerAfkChangedNotification changed =>
            new PeerAfkChangedEvent(changed.SessionId, changed.UserId, changed.IsAfk,
                changed.IsAfk ? changed.Reason : null),
        HybridSessionPeerPokedNotification poked =>
            new PeerPokedEvent(poked.SessionId, poked.UserId, poked.InitiatorUserId, poked.Message),

        HybridSessionPeerStartedVoiceShareNotification started =>
            new PeerTrackStartedEvent(started.SessionId, started.UserId, started.Inbound.ToModel()),
        HybridSessionPeerStartedVideoShareNotification started =>
            new PeerTrackStartedEvent(started.SessionId, started.UserId, started.Inbound.ToModel()),
        HybridSessionPeerStartedScreenShareNotification { Inbound: null } started =>
            new PeerScreenShareStartedEvent(started.SessionId, started.UserId, started.HasAudio),
        HybridSessionPeerStartedScreenShareNotification started =>
            new PeerTrackStartedEvent(started.SessionId, started.UserId, started.Inbound.ToModel()),

        HybridSessionPeerStoppedVoiceShareNotification stopped =>
            new PeerTrackStoppedEvent(stopped.SessionId, stopped.UserId, MediaType.Audio, MediaSourceType.Microphone),
        HybridSessionPeerStoppedVideoShareNotification stopped =>
            new PeerTrackStoppedEvent(stopped.SessionId, stopped.UserId, MediaType.Video, MediaSourceType.Camera),
        HybridSessionPeerStoppedScreenShareNotification stopped =>
            new PeerTrackStoppedEvent(stopped.SessionId, stopped.UserId, MediaType.Video, MediaSourceType.Screen),

        _ => null
    };
}
