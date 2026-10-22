using Shared.Enums;
using Shared.Enums.Coordinator;
using Shared.Enums.User;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Core.Events;
using Client.Core.Push;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;
using Client.Core.Settings;
using Client.Core.Users;
using Client.Application.Ports;

namespace Client.Application.Coordinator;

public interface ICoordinatorSession : IDisposable
{
    CoordinatorState ConnectionStatus { get; }

    /// <summary>
    /// What the coordinator reported about itself on the last login. Null until the first login succeeds.
    /// </summary>
    CoordinatorSessionModel? CoordinatorModel { get; }

    /// <summary>
    /// The address and port given to <see cref="Connect"/>, or null before the first call.
    /// </summary>
    string? ConnectedAddress { get; }

    event Action<CoordinatorState>? StateChanged; 

    /// <summary>
    /// The coordinator applied changed settings, from the control panel or from a client, this one too.
    /// <see cref="CoordinatorModel"/> already holds them. Raised on the transport thread.
    /// </summary>
    event Action<CoordinatorSessionModel>? CoordinatorReconfigured;

    /// <summary>
    /// A user connected or disconnected; carries how many are connected now, which
    /// <see cref="CoordinatorModel"/> already holds. Raised on the transport thread.
    /// </summary>
    event Action<int>? OnlineUsersChanged;
    
    event Action<ChatSessionModel>? ChatSessionAdded;
    event Action<ChatSessionModel>? ChatSessionReconfigured;
    event Action<int>? ChatSessionDeleted;
    
    event Action<HybridSessionModel>? HybridSessionAdded;
    event Action<HybridSessionModel>? HybridSessionReconfigured;
    event Action<int>? HybridSessionDeleted;

    /// <summary>
    /// The order of the sessions or their categories changed. Raised on the socket thread.
    /// </summary>
    event Action<SessionLayout>? SessionLayoutChanged;
    
    event Action<PeerJoinedEvent>? HybridSessionPeerJoined;
    event Action<PeerLeftEvent>? HybridSessionPeerLeft;
    event Action<PeerStateChangedEvent>? HybridSessionPeerStateChanged;
    event Action<PeerMicrophoneMuteChangedEvent>? HybridSessionPeerMicrophoneMuteChanged;
    event Action<PeerSoundMuteChangedEvent>? HybridSessionPeerSoundMuteChanged;
    
    event Action<int, ChatMessageModel>? TextMessageAdded;
    event Action<int, ChatMessageModel>? TextMessageEdited;
    event Action<int, int, long>? TextMessageDeleted;

    /// <summary>
    /// The read position of the user moved in a chat, here or on another device of the user.
    /// </summary>
    event Action<ChatReadState>? ChatReadStateChanged;

    /// <summary>
    /// Every peer event of every hybrid session, including the ones raised separately above.
    /// </summary>
    event Action<HybridSessionPeerEvent>? HybridSessionUpdated;

    /// <summary>
    /// The SFU node of a hybrid session went away, came back or was replaced. Raised on the socket thread.
    /// </summary>
    event Action<HybridSessionNodeEvent>? HybridSessionNodeChanged;

    /// <summary>
    /// Raised with the users the server has just sent. Raised on the thread the request completed on.
    /// </summary>
    event Action<IReadOnlyCollection<UserModel>>? UsersReceived;

    /// <summary>
    /// Raised when a user changed, the current one included. Raised on the transport thread.
    /// </summary>
    event Action<UserModel>? UserUpdated;

    /// <summary>Raised with the id of a deleted user. Raised on the transport thread.</summary>
    event Action<int>? UserDeleted;

    /// <summary>
    /// Raised with the current user after every login and reconnect, and when the server changes it.
    /// Raised on the thread the change came on.
    /// </summary>
    event Action<UserModel>? CurrentUserChanged;

    /// <summary>
    /// Round trip to the server measured on the last heartbeat; null while not connected
    /// or before the first measurement.
    /// </summary>
    TimeSpan? ServerRoundTrip { get; }

    /// <summary>
    /// Raised when <see cref="ServerRoundTrip"/> changes, about once per heartbeat interval.
    /// Raised on the transport thread.
    /// </summary>
    event Action<TimeSpan?>? ServerRoundTripChanged;

    /// <summary>
    /// Raised when a request is refused before it is handled: the server rate limit, or data
    /// outside the shared validation limits. The request itself returns its <c>Rejected</c> result,
    /// so callers leave telling the user to the subscriber of this event.
    /// Raised on the thread the request completed on.
    /// </summary>
    event Action<RequestRejectedException>? RequestRejected;

    /// <summary>
    /// The messages that precede <paramref name="beforeMessageId"/>, or the newest ones when it is null.
    /// </summary>
    Task<(FetchMessagesResult Result, ChatHistoryPage? Page)> RequestChatHistoryPage(int sessionId,
        int? beforeMessageId, int take);

    /// <summary>
    /// The messages changed after <paramref name="afterRevision"/> in their current state.
    /// </summary>
    Task<(FetchMessagesResult Result, ChatChanges? Changes)> RequestChatChanges(int sessionId,
        long afterRevision, int take);
    Task<(FetchMessageResult Result, ChatMessageModel? Message)> RequestChatMessage(int sessionId,
        int messageId);

    /// <summary>
    /// Raised before every reconnect attempt with its number and the pause before it.
    /// </summary>
    event Action<int, TimeSpan>? ReconnectScheduled;

    Task Connect(string address, int port, string? userIdentity, string? password);

    /// <summary>
    /// The certificate that stopped the last connect or reconnect with
    /// <see cref="CoordinatorState.CertificateNotTrusted"/>. Null otherwise.
    /// </summary>
    UntrustedCertificate? UntrustedCertificate { get; }

    /// <summary>
    /// The coordinator that stopped the last connect or reconnect with
    /// <see cref="CoordinatorState.CoordinatorMismatch"/>. Null otherwise.
    /// </summary>
    CoordinatorMismatch? CoordinatorMismatch { get; }

    /// <summary>
    /// Remembers the certificate as trusted for its address. The next <see cref="Connect"/> accepts it.
    /// </summary>
    Task TrustCertificateAsync(UntrustedCertificate certificate);

    /// <summary>
    /// Tells that the application left the screen. The connection is kept for a short while, so
    /// that a trip to a file picker or another application costs nothing, and is then closed
    /// without ending the session: the server counts the user as away and notifies the device
    /// instead. The state becomes <see cref="CoordinatorState.Suspended"/> at that point and stays
    /// so until <see cref="Resume"/>. A connection lost meanwhile is not restored either.
    /// </summary>
    Task Suspend();

    /// <summary>
    /// Tells that the application is back on the screen. A connection that was kept is checked; one
    /// that was closed is restored at once, through <see cref="CoordinatorState.Connecting"/>, and
    /// then as a lost one is.
    /// </summary>
    /// <returns>Completed once the restore it started ends either way, or at once without one.</returns>
    Task Resume();

    /// <summary>
    /// Closes the connection and stops a running reconnect. A lost connection is otherwise restored
    /// on its own, through <see cref="CoordinatorState.Reconnecting"/>: retried with growing pauses,
    /// and at once when the network returns, until it succeeds or the server rejects the
    /// credentials, presents an untrusted certificate or another coordinator.
    /// </summary>
    Task Disconnect();
    Task<(SessionRequestResult Result, IDictionary<int, SessionModel>? Sessions, SessionLayout? Layout)> GetAllSessions();
    Task<(SessionRequestResult Result, SessionModel? Session)> GetSessionInfo(int sessionId);
    
    Task<CreateSessionResult> CreateSession(NewSessionModel newSession);
    Task<DeleteSessionResult> DeleteSession(int sessionId);

    /// <summary>
    /// Changes of the session layout. The new layout itself comes to every client, this one too,
    /// through the layout notification.
    /// </summary>
    Task<(SessionLayoutResult Result, int? CategoryId)> CreateSessionCategory(string name);
    Task<SessionLayoutResult> RenameSessionCategory(int categoryId, string name);
    Task<SessionLayoutResult> DeleteSessionCategory(int categoryId);
    Task<SessionLayoutResult> MoveSession(int sessionId, int? categoryId, int index);
    Task<SessionLayoutResult> MoveSessionCategory(int categoryId, int index);
    
    Task<(HybridSessionJoinResult Result, TransportParameters? Transport)> JoinHybridSession(int sessionId, string clientEpoch);

    Task<HybridSessionConnectTransportResult> ConnectHybridSessionTransport(int sessionId, DtlsParameters dtlsParameters);
    Task<(StartVideoShareResult Result, OutboundTrack? Outbound)> StartVideoShareInHybridSession(int sessionId);
    Task<StopVideoShareResult> StopVideoShareInHybridSession(int sessionId);
    Task<(StartVoiceShareResult Result, OutboundTrack? Outbound)> StartVoiceShareInHybridSession(int sessionId);
    Task<StopVoiceShareResult> StopVoiceShareInHybridSession(int sessionId);
    /// <param name="withAudio">Also ask for a producer of the sound of the shared application or system.</param>
    Task<(StartScreenShareResult Result, ScreenShareOutbounds? Outbounds)> StartScreenShareInHybridSession(int sessionId,
        bool withAudio);
    Task<StopScreenShareResult> StopScreenShareInHybridSession(int sessionId);
    Task<(SetScreenShareWatchResult Result, ScreenShareInbounds? Inbounds)> SetScreenShareWatchInHybridSession(int sessionId,
        int userId, bool isWatching);

    /// <summary>
    /// Tells the SFU that the transport is ready to receive the given inbound tracks. The SFU sends
    /// nothing in a track until then.
    /// </summary>
    Task<ResumeInboundsResult> ResumeInboundsInHybridSession(int sessionId, IReadOnlyList<string> inboundIds);
    Task<SetMicrophoneMuteResult> SetMicrophoneMuteInHybridSession(int sessionId, bool isMuted);

    /// <summary>
    /// Tells the server that we turned the sound of the session off or on, for the others to see it.
    /// The sound itself is silenced on this side.
    /// </summary>
    Task<SetSoundMuteResult> SetSoundMuteInHybridSession(int sessionId, bool isMuted);
    Task<(PeerListRequestResult Result, Dictionary<int, PeerModel>? Peers)> GetAllHybridSessionPeers(int sessionId);
    Task<LeaveSessionResult> LeaveSession(int sessionId);

    /// <summary>
    /// Removes another user from the hybrid session. Kicking ourselves is refused without asking the server.
    /// </summary>
    Task<LeaveSessionResult> KickFromHybridSession(int sessionId, int userId);

    /// <summary>
    /// Replaces the settings of the hybrid session. A saved change comes back to every client, this one too,
    /// as <see cref="HybridSessionReconfigured"/>.
    /// </summary>
    Task<ChangeSessionSettingsResult> ChangeHybridSessionSettings(int sessionId, string name, int capacity,
        bool isHostMustApprove);
    bool ValidateSession(NewSessionModel newSession);
    
    Task<UserModel?> GetUser(int userId);

    /// <summary>
    /// Gets several users in one request. Users the server does not know are missing from the result.
    /// </summary>
    Task<IReadOnlyDictionary<int, UserModel>> GetUsers(IReadOnlyCollection<int> userIds);

    /// <summary>
    /// The user was deleted: the server answered without it, or announced the deletion. False for a
    /// user missing from a result only because the request failed.
    /// </summary>
    bool IsUserDeleted(int userId);

    /// <summary>
    /// Finds users by name for a mention: those whose name starts with the query first, then those
    /// containing it. An empty query gives the users online first. Empty when the request failed.
    /// </summary>
    Task<IReadOnlyList<UserModel>> SearchUsers(string query, int limit);

    /// <summary>Every user of the coordinator, banned ones too, by name.</summary>
    Task<(UsersRequestResult Result, IReadOnlyList<UserModel>? Users)> GetAllUsers();

    UserModel GetCurrentUser();
    SavedUserModel GetCurrentUserCredentials();

    /// <summary>
    /// Renames the current user. On success <see cref="GetCurrentUser"/> returns the new name
    /// and <see cref="CurrentUserChanged"/> is raised.
    /// </summary>
    Task<ChangeUsernameResult> ChangeUsername(string username);

    /// <summary>
    /// Renames a user, connected or not. On success <see cref="UserUpdated"/> is raised with the new name.
    /// </summary>
    Task<ChangeUsernameResult> ChangeUsername(int userId, string username);

    /// <summary>
    /// Bans, freezes or reinstates another user. On success <see cref="UserUpdated"/> is raised with the new state.
    /// </summary>
    Task<ModerateUserResult> SetUserState(int userId, UserState state);

    /// <summary>Deletes another user. On success <see cref="UserDeleted"/> is raised.</summary>
    Task<ModerateUserResult> DeleteUser(int userId);
    bool ValidateUsername(string username);
    
    Task<AddMessageResult> SendChatMessage(NewChatMessage message);

    /// <summary>
    /// Uploads a picture and puts it on the profile of the current user. On success
    /// <see cref="UserUpdated"/> and <see cref="CurrentUserChanged"/> are raised with its address.
    /// </summary>
    /// <param name="image">The encoded picture, as small as an avatar is shown.</param>
    /// <returns>The user as the server has them now, when the picture was taken.</returns>
    Task<(SetAvatarResult Result, UserModel? User)> SetAvatar(byte[] image, string mimeType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the status of the current user. On success <see cref="UserUpdated"/> and
    /// <see cref="CurrentUserChanged"/> are raised with it.
    /// </summary>
    Task<SetUserStatusResult> SetStatus(UserStatus status);

    /// <summary>
    /// Takes the avatar off a connected user. On success <see cref="UserUpdated"/> is raised.
    /// </summary>
    Task<SetAvatarResult> ResetAvatar(int userId);

    /// <inheritdoc cref="Client.Application.Ports.ICoordinatorGateway.DownloadAvatar"/>
    Task<MediaTransferResult> DownloadAvatar(string avatarId, Stream destination,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Client.Application.Ports.ICoordinatorGateway.UploadMedia"/>
    Task<MediaUploadOutcome> UploadMedia(MediaUpload upload, IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Client.Application.Ports.ICoordinatorGateway.DownloadChatImage"/>
    Task<MediaTransferResult> DownloadChatImage(int imageId, bool preview, Stream destination,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Client.Application.Ports.ICoordinatorGateway.DownloadChatFile"/>
    Task<MediaTransferResult> DownloadChatFile(int fileId, Stream destination,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default);
    Task<DeleteMessageResult> DeleteChatMessage(int chatId, int messageId);

    /// <summary>
    /// Replaces the text of the user's own message. On success <see cref="TextMessageEdited"/> is raised.
    /// </summary>
    Task<EditMessageResult> EditChatMessage(int chatId, int messageId, ChatTextContent text);

    /// <summary>
    /// The read positions and unread counts of all chats of the user.
    /// </summary>
    Task<(ChatReadResult Result, IReadOnlyList<ChatReadState>? States)> RequestChatReadStates();

    /// <summary>
    /// Moves the read position of the user in a chat up to <paramref name="messageId"/>; it never
    /// goes back. Every device of the user gets <see cref="ChatReadStateChanged"/>.
    /// </summary>
    Task<(ChatReadResult Result, ChatReadState? State)> MarkChatRead(int chatId, int messageId);

    /// <summary>
    /// Registers this device for the push notifications of the coordinator, encrypted with
    /// <paramref name="key"/>. Registering a token again replaces its key.
    /// </summary>
    /// <returns>On success also the token the device answers notifications with over HTTP.</returns>
    Task<(PushTokenResult Result, string? ReplyToken)> RegisterPushToken(string token, PushPlatform platform, PushKey key);
    Task<PushTokenResult> UnregisterPushToken(string token);

    /// <summary>The stored settings of the coordinator and the SFU nodes they may name.</summary>
    Task<(CoordinatorSettingsResult Result, CoordinatorSettingsSnapshot? Snapshot)> GetCoordinatorSettings();

    /// <summary>
    /// Replaces the settings of the coordinator. Refused with <see cref="CoordinatorSettingsResult.Conflict"/>
    /// when someone saved others after <paramref name="expectedRevision"/>; the outcome then holds theirs.
    /// A saved change comes back to every client, this one too, as <see cref="CoordinatorReconfigured"/>.
    /// A new address or port restarts the coordinator, which drops the connection.
    /// </summary>
    Task<CoordinatorSettingsUpdateOutcome> UpdateCoordinatorSettings(CoordinatorSettingsUpdate update,
        long expectedRevision);

    /// <summary>
    /// What the current user may do, after the roles of the coordinator and the roles of the user. Nothing before
    /// the first login.
    /// </summary>
    CoordinatorPermissions Permissions { get; }

    /// <summary>The roles of the coordinator, lowest first, as of the last login or change.</summary>
    IReadOnlyList<RoleModel> Roles { get; }

    /// <summary>
    /// A role changed, or the roles of the current user did, wherever the change was made. Raised on the thread
    /// the change came on.
    /// </summary>
    event Action<CoordinatorPermissions>? PermissionsChanged;

    /// <summary>The roles of the coordinator, lowest first.</summary>
    Task<(CoordinatorRoleResult Result, IReadOnlyList<RoleModel>? Roles)> GetRoles();

    /// <summary>Creates a role right above the everyone role. Every client hears of it.</summary>
    Task<(CoordinatorRoleResult Result, RoleModel? Role)> CreateRole(RoleChange change);

    /// <summary>
    /// Changes a role, unless it was changed after <paramref name="expectedRevision"/>: then the result is
    /// <see cref="CoordinatorRoleResult.Conflict"/> with the role as it is now.
    /// </summary>
    Task<(CoordinatorRoleResult Result, RoleModel? Role)> UpdateRole(int roleId, RoleChange change,
        long expectedRevision);

    /// <summary>Deletes a role and takes it from every user who has it.</summary>
    Task<CoordinatorRoleResult> DeleteRole(int roleId);

    /// <param name="roleIds">Every role but the everyone role, lowest first.</param>
    Task<(CoordinatorRoleResult Result, IReadOnlyList<RoleModel>? Roles)> ReorderRoles(IReadOnlyList<int> roleIds);

    /// <summary>
    /// Gives a role to a user or takes it. The user comes to every client changed, as <see cref="UserUpdated"/>.
    /// </summary>
    Task<(CoordinatorRoleResult Result, IReadOnlyList<int>? RoleIds)> SetUserRole(int userId, int roleId, bool has);

    string GetCoordinatorName(bool trimName = true);
}