using Shared.Enums;
using Shared.Models.Media;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;
using Client.Core.Users;

namespace Client.Core.Events;

/// <summary>
/// Something the coordinator reported on its own, outside of a request.
/// </summary>
public abstract record CoordinatorEvent;

public sealed record ServerClosingEvent(string? Message) : CoordinatorEvent;

/// <summary>
/// The settings of the coordinator were changed and applied, from the control panel or by a user.
/// </summary>
public sealed record CoordinatorReconfiguredEvent(CoordinatorSessionModel Coordinator) : CoordinatorEvent;

/// <summary>
/// A role was created, changed, moved or deleted, from the control panel or by a user. Carries all roles,
/// lowest first.
/// </summary>
public sealed record RolesChangedEvent(IReadOnlyList<RoleModel> Roles) : CoordinatorEvent;

/// <summary>
/// Round trip of the last heartbeat ping answered by the server.
/// </summary>
public sealed record ServerRoundTripMeasuredEvent(TimeSpan RoundTrip) : CoordinatorEvent;

/// <summary>
/// The server is about to restart and expects the clients to come back.
/// </summary>
public sealed record ServerRestartingEvent(string? Message) : CoordinatorEvent;

/// <summary>
/// A user of the coordinator changed, for example renamed themselves.
/// </summary>
public sealed record UserUpdatedEvent(UserModel User) : CoordinatorEvent;

/// <summary>A user of the coordinator was deleted.</summary>
public sealed record UserDeletedEvent(int UserId) : CoordinatorEvent;

/// <summary>A user connected or disconnected; carries how many are connected now.</summary>
public sealed record OnlineUsersChangedEvent(int ConnectedUsersCount) : CoordinatorEvent;

public sealed record SessionAddedEvent(SessionModel Session) : CoordinatorEvent;

public sealed record SessionReconfiguredEvent(SessionModel Session) : CoordinatorEvent;

public sealed record SessionDeletedEvent(int SessionId, SessionType SessionType) : CoordinatorEvent;

/// <summary>
/// The order of the sessions or their categories changed.
/// </summary>
public sealed record SessionLayoutChangedEvent(SessionLayout Layout) : CoordinatorEvent;

/// <param name="Message">The message as the server sent it, so <see cref="ChatMessageModel.IsMine"/> is not set yet.</param>
public sealed record ChatMessageAddedEvent(int ChatId, ChatMessageModel Message) : CoordinatorEvent;

/// <inheritdoc cref="ChatMessageAddedEvent"/>
public sealed record ChatMessageEditedEvent(int ChatId, ChatMessageModel Message) : CoordinatorEvent;

/// <param name="Revision">The revision of the chat the deletion took.</param>
public sealed record ChatMessageDeletedEvent(int ChatId, int MessageId, long Revision) : CoordinatorEvent;

/// <summary>
/// The read position of the user moved in a chat, on this device or on another one.
/// </summary>
public sealed record ChatReadStateChangedEvent(ChatReadState State) : CoordinatorEvent;

/// <summary>
/// A change of the SFU node a hybrid session runs on.
/// </summary>
public abstract record HybridSessionNodeEvent(int SessionId) : CoordinatorEvent;

/// <summary>
/// The session lost its SFU node and waits for one, or got one again. The peers stay in the session meanwhile.
/// </summary>
public sealed record SessionNodeStateChangedEvent(int SessionId, HybridSessionNodeState State)
    : HybridSessionNodeEvent(SessionId);

/// <summary>
/// The server dropped what the SFU held for the session: it moved to another node, or its node came back.
/// The peers have to rejoin.
/// </summary>
public sealed record SessionNodeReplacedEvent(int SessionId) : HybridSessionNodeEvent(SessionId);

/// <summary>
/// A change of one peer of a hybrid session.
/// </summary>
public abstract record HybridSessionPeerEvent(int SessionId, int UserId) : CoordinatorEvent;

public sealed record PeerJoinedEvent(int SessionId, PeerModel Peer) : HybridSessionPeerEvent(SessionId, Peer.UserId);

public record PeerLeftEvent(int SessionId, int UserId) : HybridSessionPeerEvent(SessionId, UserId);

/// <summary>
/// Another user or the server (without an initiator) removed the peer from the session.
/// Everything that handles a peer leaving handles it too.
/// </summary>
public sealed record PeerKickedEvent(int SessionId, int UserId, int? InitiatorUserId, string? Reason)
    : PeerLeftEvent(SessionId, UserId);

/// <summary>
/// The host or a user who approves members turned away a peer waiting to be let in, which takes it out of the session.
/// </summary>
public sealed record PeerRejectedEvent(int SessionId, int UserId, int? InitiatorUserId)
    : PeerLeftEvent(SessionId, UserId);

public sealed record PeerStateChangedEvent(int SessionId, int UserId, HybridSessionPeerState State)
    : HybridSessionPeerEvent(SessionId, UserId);

public sealed record PeerMicrophoneMuteChangedEvent(int SessionId, int UserId, bool IsMuted)
    : HybridSessionPeerEvent(SessionId, UserId);

public sealed record PeerSoundMuteChangedEvent(int SessionId, int UserId, bool IsMuted)
    : HybridSessionPeerEvent(SessionId, UserId);

/// <param name="HasAudio">The share carries the sound of the shared application or system.</param>
public sealed record PeerScreenShareStartedEvent(int SessionId, int UserId, bool HasAudio = false)
    : HybridSessionPeerEvent(SessionId, UserId);

/// <summary>
/// The peer started sending a track: a microphone, a camera or a screen.
/// </summary>
public sealed record PeerTrackStartedEvent(int SessionId, int UserId, InboundTrack Track)
    : HybridSessionPeerEvent(SessionId, UserId);

public sealed record PeerTrackStoppedEvent(int SessionId, int UserId, MediaType MediaType, MediaSourceType SourceType)
    : HybridSessionPeerEvent(SessionId, UserId);
