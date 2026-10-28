using Shared.Permissions;
using Shared.Enums;
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

    /// <summary>
    /// The system verified the certificate of the last connection; false when the user trusted it by hand.
    /// </summary>
    bool IsCertificateVerified { get; }

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
    event Action<PeerAfkChangedEvent>? HybridSessionPeerAfkChanged;
    event Action<PeerPokedEvent>? HybridSessionPeerPoked;
    
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
    /// Raised when a request is refused as a whole: the server rate limit, data outside the shared validation
    /// limits, missing permissions or a frozen user. The request itself returns that error, so callers leave
    /// telling the user to the subscriber of this event.
    /// Raised on the thread the request completed on.
    /// </summary>
    event Action<RequestFailedException>? RequestRejected;

    /// <summary>
    /// The messages that precede <paramref name="beforeMessageId"/>, or the newest ones when it is null.
    /// </summary>
    Task<RequestResult<ChatHistoryPage>> RequestChatHistoryPage(int sessionId,
        int? beforeMessageId, int take);

    /// <summary>
    /// The messages changed after <paramref name="afterRevision"/> in their current state.
    /// </summary>
    Task<RequestResult<ChatChanges>> RequestChatChanges(int sessionId,
        long afterRevision, int take);
    Task<RequestResult<ChatMessageModel>> RequestChatMessage(int sessionId,
        int messageId);

    /// <summary>
    /// Raised before every reconnect attempt with its number and the pause before it.
    /// </summary>
    event Action<int, TimeSpan>? ReconnectScheduled;

    /// <param name="password">The password of the server.</param>
    /// <param name="accountPassword">
    /// The password of the user's account kept with the saved identity; it finishes the login without asking.
    /// </param>
    Task Connect(string address, int port, string? userIdentity, string? password, string? accountPassword = null);

    /// <summary>
    /// Asks the user for the account password or a one-time code when a login, a reconnect too, needs them.
    /// Without it such a login ends with <see cref="CoordinatorState.AuthenticationNeeded"/>. Called on a thread
    /// of the pool.
    /// </summary>
    LoginPromptHandler? LoginPrompt { get; set; }

    /// <summary>
    /// The account password to keep with the saved identity: the one the user logs in with, when they asked
    /// to remember it. Null when nothing is to be kept.
    /// </summary>
    string? AccountPasswordToSave { get; }

    /// <summary>
    /// Raised when <see cref="AccountPasswordToSave"/> changes: a login with another password, the password
    /// changed or removed in the profile. Raised on any thread.
    /// </summary>
    event Action<string?>? AccountPasswordToSaveChanged;

    /// <summary>What the current user logs in with.</summary>
    Task<RequestResult<AccountSecurity>> GetAccountSecurity();

    /// <summary>
    /// Sets the password of the current user, or removes it when <paramref name="newPassword"/> is null. The
    /// next reconnect logs in with the new one.
    /// </summary>
    Task<RequestResult<AccountSecurity>> SetAccountPassword(AccountProof proof,
        string? newPassword);

    /// <summary>Offers a secret for an authenticator app; <see cref="EnableTwoFactor"/> turns the codes on with it.</summary>
    Task<RequestResult<TwoFactorSetup>> SetupTwoFactor(AccountProof proof);
    Task<RequestResult<AccountSecurity>> EnableTwoFactor(string code);
    Task<RequestResult<AccountSecurity>> DisableTwoFactor(AccountProof proof);
    Task<RequestResult<AccountSecurity>> DeletePasskey(AccountProof proof, int passkeyId);

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
    Task<RequestResult<(IDictionary<int, SessionModel> Sessions, SessionLayout Layout)>> GetAllSessions();
    Task<RequestResult<SessionModel>> GetSessionInfo(int sessionId);
    
    Task<RequestResult> CreateSession(NewSessionModel newSession);
    Task<RequestResult> DeleteSession(int sessionId);

    /// <summary>
    /// Changes of the session layout. The new layout itself comes to every client, this one too,
    /// through the layout notification.
    /// </summary>
    Task<RequestResult<int>> CreateSessionCategory(string name);
    Task<RequestResult> RenameSessionCategory(int categoryId, string name);
    Task<RequestResult> DeleteSessionCategory(int categoryId);
    Task<RequestResult> MoveSession(int sessionId, int? categoryId, int index);
    Task<RequestResult> MoveSessionCategory(int categoryId, int index);
    
    Task<RequestResult<TransportParameters>> JoinHybridSession(int sessionId, string clientEpoch);

    Task<RequestResult> ConnectHybridSessionTransport(int sessionId, DtlsParameters dtlsParameters);
    Task<RequestResult<OutboundTrack>> StartVideoShareInHybridSession(int sessionId);
    Task<RequestResult> StopVideoShareInHybridSession(int sessionId);
    Task<RequestResult<OutboundTrack>> StartVoiceShareInHybridSession(int sessionId);
    Task<RequestResult> StopVoiceShareInHybridSession(int sessionId);
    /// <param name="withAudio">Also ask for a producer of the sound of the shared application or system.</param>
    Task<RequestResult<ScreenShareOutbounds>> StartScreenShareInHybridSession(int sessionId,
        bool withAudio);
    Task<RequestResult> StopScreenShareInHybridSession(int sessionId);
    Task<RequestResult<ScreenShareInbounds?>> SetScreenShareWatchInHybridSession(int sessionId,
        int userId, bool isWatching);

    /// <summary>
    /// Tells the SFU that the transport is ready to receive the given inbound tracks. The SFU sends
    /// nothing in a track until then.
    /// </summary>
    Task<RequestResult> ResumeInboundsInHybridSession(int sessionId, IReadOnlyList<string> inboundIds);
    Task<RequestResult> SetMicrophoneMuteInHybridSession(int sessionId, bool isMuted);

    /// <summary>
    /// Tells the server that we turned the sound of the session off or on, for the others to see it.
    /// The sound itself is silenced on this side.
    /// </summary>
    Task<RequestResult> SetSoundMuteInHybridSession(int sessionId, bool isMuted);

    /// <summary>Tells the others of the session that we are away from the keyboard, and why, or that we are back.</summary>
    Task<RequestResult> SetAfkInHybridSession(int sessionId, bool isAfk, string? reason);

    /// <summary>Pokes another peer of the session; only they see it, with the message if there is one.</summary>
    Task<RequestResult> PokeInHybridSession(int sessionId, int userId, string? message);
    Task<RequestResult<Dictionary<int, PeerModel>>> GetAllHybridSessionPeers(int sessionId);
    Task<RequestResult> LeaveSession(int sessionId);

    /// <summary>
    /// Removes another user from the hybrid session. Kicking ourselves is refused without asking the server.
    /// </summary>
    Task<RequestResult> KickFromHybridSession(int sessionId, int userId);

    /// <summary>Lets a user waiting for the host into the hybrid session.</summary>
    Task<RequestResult> ApproveInHybridSession(int sessionId, int userId);

    /// <summary>Turns away a user waiting for the host; they cannot join the session again while it runs.</summary>
    Task<RequestResult> RejectInHybridSession(int sessionId, int userId);

    /// <summary>
    /// Replaces the settings of the hybrid session. A saved change comes back to every client, this one too,
    /// as <see cref="HybridSessionReconfigured"/>.
    /// </summary>
    Task<RequestResult> ChangeHybridSessionSettings(int sessionId, string name, int capacity,
        bool isHostMustApprove, MediaSettings media);

    /// <summary>
    /// Replaces the permission overrides of the session. A saved change comes back as the session reconfigured,
    /// or removed for a client that may no longer see it.
    /// </summary>
    Task<RequestResult> ChangeSessionPermissions(int sessionId,
        IReadOnlyList<PermissionOverride> overrides);
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
    Task<RequestResult<IReadOnlyList<UserModel>>> GetAllUsers();

    UserModel GetCurrentUser();
    SavedUserModel GetCurrentUserCredentials();

    /// <summary>
    /// Renames the current user. On success <see cref="GetCurrentUser"/> returns the new name
    /// and <see cref="CurrentUserChanged"/> is raised.
    /// </summary>
    Task<RequestResult> ChangeUsername(string username);

    /// <summary>
    /// Renames a user, connected or not. On success <see cref="UserUpdated"/> is raised with the new name.
    /// </summary>
    Task<RequestResult> ChangeUsername(int userId, string username);

    /// <summary>
    /// Bans, freezes or reinstates another user. On success <see cref="UserUpdated"/> is raised with the new state.
    /// </summary>
    Task<RequestResult> SetUserState(int userId, UserState state);

    /// <summary>Deletes another user. On success <see cref="UserDeleted"/> is raised.</summary>
    Task<RequestResult> DeleteUser(int userId);
    bool ValidateUsername(string username);
    
    Task<RequestResult> SendChatMessage(NewChatMessage message);

    /// <summary>
    /// Uploads a picture and puts it on the profile of the current user. On success
    /// <see cref="UserUpdated"/> and <see cref="CurrentUserChanged"/> are raised with its address.
    /// </summary>
    /// <param name="image">The encoded picture, as small as an avatar is shown.</param>
    /// <returns>The user as the server has them now, when the picture was taken.</returns>
    Task<RequestResult<UserModel>> SetAvatar(byte[] image, string mimeType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the status of the current user. On success <see cref="UserUpdated"/> and
    /// <see cref="CurrentUserChanged"/> are raised with it.
    /// </summary>
    Task<RequestResult> SetStatus(UserStatus status);

    /// <summary>
    /// Takes the avatar off a connected user. On success <see cref="UserUpdated"/> is raised.
    /// </summary>
    Task<RequestResult> ResetAvatar(int userId);

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
    Task<RequestResult> DeleteChatMessage(int chatId, int messageId);

    /// <summary>
    /// Replaces the text of the user's own message. On success <see cref="TextMessageEdited"/> is raised.
    /// </summary>
    Task<RequestResult> EditChatMessage(int chatId, int messageId, ChatTextContent text);

    /// <summary>
    /// The read positions and unread counts of all chats of the user.
    /// </summary>
    Task<RequestResult<IReadOnlyList<ChatReadState>>> RequestChatReadStates();

    /// <summary>
    /// Moves the read position of the user in a chat up to <paramref name="messageId"/>; it never
    /// goes back. Every device of the user gets <see cref="ChatReadStateChanged"/>.
    /// </summary>
    Task<RequestResult<ChatReadState>> MarkChatRead(int chatId, int messageId);

    /// <summary>
    /// Registers this device for the push notifications of the coordinator, encrypted with
    /// <paramref name="key"/>. Registering a token again replaces its key.
    /// </summary>
    /// <returns>On success also the token the device answers notifications with over HTTP.</returns>
    Task<RequestResult<string?>> RegisterPushToken(string token, PushPlatform platform, PushKey key);
    Task<RequestResult> UnregisterPushToken(string token);

    /// <summary>The stored settings of the coordinator and the SFU nodes they may name.</summary>
    Task<RequestResult<CoordinatorSettingsSnapshot>> GetCoordinatorSettings();

    /// <summary>
    /// Replaces the settings of the coordinator and returns them as saved. Refused with
    /// <see cref="Shared.Enums.ErrorCode.Conflict"/> when someone saved others after <paramref name="expectedRevision"/>,
    /// which are fetched again then. A saved change comes back to every client, this one too, as
    /// <see cref="CoordinatorReconfigured"/>. A new address or port restarts the coordinator, which drops the connection.
    /// </summary>
    Task<RequestResult<CoordinatorSettingsModel>> UpdateCoordinatorSettings(CoordinatorSettingsUpdate update,
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
    Task<RequestResult<IReadOnlyList<RoleModel>>> GetRoles();

    /// <summary>Creates a role right above the everyone role. Every client hears of it.</summary>
    Task<RequestResult<RoleModel>> CreateRole(RoleChange change);

    /// <summary>
    /// Changes a role, unless it was changed after <paramref name="expectedRevision"/>: then it fails with
    /// <see cref="Shared.Enums.ErrorCode.Conflict"/>.
    /// </summary>
    Task<RequestResult<RoleModel>> UpdateRole(int roleId, RoleChange change,
        long expectedRevision);

    /// <summary>Deletes a role and takes it from every user who has it.</summary>
    Task<RequestResult> DeleteRole(int roleId);

    /// <param name="roleIds">Every role but the everyone role, lowest first.</param>
    Task<RequestResult<IReadOnlyList<RoleModel>>> ReorderRoles(IReadOnlyList<int> roleIds);

    /// <summary>
    /// Gives a role to a user or takes it. The user comes to every client changed, as <see cref="UserUpdated"/>.
    /// </summary>
    Task<RequestResult<IReadOnlyList<int>>> SetUserRole(int userId, int roleId, bool has);

    string GetCoordinatorName(bool trimName = true);
}