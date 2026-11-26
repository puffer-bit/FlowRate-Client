using Shared.Permissions;
using Shared.Enums;
using Shared.Enums.User;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Core.Push;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;
using Client.Core.Settings;
using Client.Core.Users;
using Client.Application.Ports;

namespace Client.Application.Coordinator;

public interface ICoordinatorSession : IDisposable
{
    /// <summary>A call in one of the user's chats started, changed or ended.</summary>
    event Action<Shared.Models.DataTransferObjects.ChatCallDTO>? ChatCallChanged;
    event Action<ErrorCode>? ChatCallFailed;
    /// <summary>The running calls of the user's chats, by chat.</summary>
    IReadOnlyDictionary<int, Shared.Models.DataTransferObjects.ChatCallDTO> ChatCalls { get; }
    /// <summary>The running call this device takes part in.</summary>
    Shared.Models.DataTransferObjects.ChatCallDTO? CurrentCall { get; }
    /// <param name="targetUserId">Whom Ring calls again; null rings every member not in the call.</param>
    Task<RequestResult<Shared.Models.DataTransferObjects.ChatCallDTO>> ChangeChatCall(int chatId, Guid callId,
        Shared.Models.DataTransferObjects.ChatCallAction action, int? targetUserId = null);

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

    /// <summary>The host given to <see cref="Connect"/>, as it was typed; null before the first call.</summary>
    string? ConnectedHost { get; }

    /// <summary>The port given to <see cref="Connect"/>.</summary>
    int ConnectedPort { get; }

    /// <summary>
    /// The certificate the user trusted by hand that the last connection was made with, so a link to the server can
    /// carry it; null when the system verified the certificate.
    /// </summary>
    string? TrustedFingerprint { get; }

    event Action<CoordinatorState>? StateChanged; 

    /// <summary>
    /// The coordinator applied changed settings, from the control panel or from a client, this one too.
    /// <see cref="CoordinatorModel"/> already holds them. Raised on the transport thread.
    /// </summary>
    event Action<CoordinatorSessionModel>? CoordinatorReconfigured;

    /// <summary>
    /// The server warned that it goes down, or goes down now (<see cref="ServerRestartEvent.At"/> null). Raised on the
    /// transport thread.
    /// </summary>
    event Action<ServerRestartEvent>? ServerRestart;

    /// <summary>The announced restart is called off. Raised on the transport thread.</summary>
    event Action? ServerRestartCancelled;

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
    /// <summary>Active authors and their original expiry times, including chats not opened yet.</summary>
    IReadOnlyDictionary<int, DateTimeOffset> GetChatTypingUsers(int chatId);

    event Action<int, int, bool>? ChatUserTyping;
    event Action<ChatReadState>? ChatReadStateChanged;
    /// <summary>Another member of a direct chat or a group read further. Raised on the socket thread.</summary>
    event Action<ChatPeerRead>? ChatPeerRead;
    event Action<ChatViewersChangedEvent>? ChatViewersChanged;

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

    /// <param name="password">The password of the server, asked of every login while it has one.</param>
    /// <param name="login">
    /// How to log in. A plan that adds a device (registration, recovery phrase, username, login code, approval by
    /// another device, the identity of the earlier protocol) leaves the session logging in as that device, which
    /// <see cref="GetCurrentUserCredentials"/> then tells.
    /// </param>
    /// <param name="expectedCoordinatorId">The coordinator owning the saved account; a different ID rejects the connection.</param>
    Task Connect(string address, int port, string? password, LoginPlan login, string? expectedCoordinatorId = null);

    /// <summary>
    /// Asks the user for the account password or a one-time code when a login by username needs them. Without it
    /// such a login ends with <see cref="CoordinatorState.AuthenticationNeeded"/>. Called on a thread of the pool.
    /// </summary>
    LoginPromptHandler? LoginPrompt { get; set; }

    /// <summary>The device is temporary: its key lives in memory, and it logs out when disconnected.</summary>
    bool IsTemporaryDevice { get; }

    /// <summary>
    /// The device is not one of the account any more: removed from another device or the control panel (with the
    /// reason), or unknown to the server or without its key at a login (null). Its key is dropped, and the session
    /// does not reconnect on its own. Raised on any thread.
    /// </summary>
    event Action<Shared.Enums.Auth.DeviceEndReason?>? DeviceEnded;

    /// <summary>A device was added to the account, from here or elsewhere. Raised on any thread.</summary>
    event Action<AccountDevice>? DeviceAdded;

    /// <summary>The code a new device shows while it waits for a device of the account to approve it.</summary>
    event Action<DeviceLinkCode>? DeviceLinkCodeReceived;

    /// <summary>The phrase of an account this session just registered or moved over; null once taken.</summary>
    string? TakeNewRecoveryPhrase();

    /// <summary>The devices of the account, the latest used first.</summary>
    Task<RequestResult<IReadOnlyList<AccountDevice>>> GetAccountDevices();

    /// <summary>Removes another device of the account, or all of them when <paramref name="deviceId"/> is null.</summary>
    Task<RequestResult<IReadOnlyList<AccountDevice>>> RevokeAccountDevice(Guid? deviceId);

    /// <summary>Removes this device from the account and disconnects; the saved account then needs a login.</summary>
    Task<RequestResult> Logout();

    /// <summary>What the new device that shows the code tells about itself.</summary>
    Task<RequestResult<DeviceLinkInfo>> GetDeviceLinkInfo(string code);

    /// <summary>Adds the new device that shows the code to the account, or turns it away.</summary>
    Task<RequestResult> ApproveDeviceLink(string code, bool approve);

    /// <summary>Replaces the recovery phrase with a new one, answered; the old one stops working.</summary>
    Task<RequestResult<string>> ResetRecoveryPhrase(AccountProof proof);

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
    
    Task<RequestResult<SessionModel>> CreateConversation(NewSessionModel session) => throw new NotSupportedException();

    Task<RequestResult> CreateSession(NewSessionModel newSession);
    Task<RequestResult> ChangeGroup(int sessionId, string name, int[] memberIds, int ownerId)
        => throw new NotSupportedException();
    Task<RequestResult> LeaveGroup(int sessionId) => throw new NotSupportedException();

    /// <summary>
    /// The owner uploads a picture and puts it on a group, or takes the picture off when <paramref name="image"/>
    /// is null. The group comes back changed like after <see cref="ChangeGroup"/>.
    /// Fails with <see cref="ErrorCode.UploadNotFound"/> when the image is empty or too large, or did not get to the server.
    /// </summary>
    Task<RequestResult> SetGroupAvatar(int sessionId, byte[]? image, string? mimeType,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

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
    
    Task<RequestResult<TransportParameters?>> JoinHybridSession(int sessionId, string clientEpoch);

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

    /// <summary>
    /// Moves another user from one hybrid session to another; the server keeps a place for them there
    /// and their client joins it.
    /// </summary>
    Task<RequestResult> MoveHybridSessionPeer(int sessionId, int userId, int targetSessionId);

    /// <summary>Lets a user waiting for the host into the hybrid session.</summary>
    Task<RequestResult> ApproveInHybridSession(int sessionId, int userId);

    /// <summary>Turns away a user waiting for the host; they cannot join the session again while it runs.</summary>
    Task<RequestResult> RejectInHybridSession(int sessionId, int userId);

    /// <summary>Updates a server chat channel name and description and broadcasts its saved settings.</summary>
    Task<RequestResult> ChangeChatSessionSettings(int sessionId, string name, string? description);

    /// <summary>
    /// Replaces the settings of the hybrid session. A saved change comes back to every client, this one too,
    /// as <see cref="HybridSessionReconfigured"/>.
    /// </summary>
    Task<RequestResult> ChangeHybridSessionSettings(int sessionId, string name, int capacity,
        bool isApprovalRequired, MediaSettings media);

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

    Task<RequestResult<IReadOnlyList<UserModel>>> SetChatViewersSubscription(int chatId,
        Guid subscriptionId, bool subscribe);

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
    /// <summary>Freezes (<see cref="UserState.Suspended"/>) or reinstates (<see cref="UserState.Normal"/>) a user.</summary>
    Task<RequestResult> SetUserState(int userId, UserState state);

    Task<RequestResult> BanUser(int userId, BanKind kind, string? reason = null, DateTimeOffset? expiresAt = null, string? address = null);

    /// <summary>Lifts every ban made for the user, by account and by IP.</summary>
    Task<RequestResult> UnbanUser(int userId);

    /// <summary>Deletes another user. On success <see cref="UserDeleted"/> is raised.</summary>
    Task<RequestResult<System.Collections.Generic.IReadOnlyList<Shared.Models.DataTransferObjects.BanDTO>>> GetBans();
    Task<RequestResult> RevokeBan(Guid banId);
    string? BanReason => null;
    Task<RequestResult> DeleteUser(int userId);
    bool ValidateUsername(string username);
    
    Task<RequestResult> SetChatTyping(int sessionId, bool isTyping);

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
    /// Tells the server how long this device has gone without input. While the user picked online and every
    /// device of theirs is idle, others see them idle or sleeping. Not saved: a new connection starts active.
    /// </summary>
    Task<RequestResult> SetIdleLevel(IdleLevel level);

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

    /// <inheritdoc cref="Client.Application.Ports.ICoordinatorGateway.DownloadChatFilePreview"/>
    Task<MediaTransferResult> DownloadChatFilePreview(int fileId, Stream destination,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Client.Application.Ports.ICoordinatorGateway.DownloadLinkPreviewImage"/>
    Task<MediaTransferResult> DownloadLinkPreviewImage(int previewId, Stream destination,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Client.Application.Ports.ICoordinatorGateway.DownloadChatImage"/>
    Task<MediaTransferResult> DownloadChatImage(int imageId, bool preview, Stream destination,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Client.Application.Ports.ICoordinatorGateway.DownloadChatFile"/>
    Task<MediaTransferResult> DownloadChatFile(int fileId, Stream destination,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="Client.Application.Ports.ICoordinatorGateway.DownloadChatFileRange"/>
    Task<MediaTransferResult> DownloadChatFileRange(int fileId, long offset, long length, Stream destination,
        CancellationToken cancellationToken = default);
    Task<RequestResult> DeleteChatMessage(int chatId, int messageId);

    /// <summary>
    /// Replaces the text of the user's own message. On success <see cref="TextMessageEdited"/> is raised.
    /// </summary>
    /// <param name="isLinkPreviewDisabled">Turns the preview of the link off or on; null leaves it as it was.</param>
    Task<RequestResult> EditChatMessage(int chatId, int messageId, ChatTextContent text, bool? isLinkPreviewDisabled = null);

    /// <inheritdoc cref="Client.Application.Ports.ICoordinatorGateway.RequestLinkPreview"/>
    Task<RequestResult<ChatLinkPreview?>> RequestLinkPreview(int chatId, string url);

    /// <summary>Sets the current user's reaction; updates arrive through TextMessageEdited.</summary>
    Task<RequestResult<ChatHistoryPage>> RequestChatPinnedMessages(int chatId);
    Task<RequestResult<ChatHistoryWindow>> RequestChatHistoryWindow(int chatId, int messageId,
        Shared.Models.Requests.SessionActions.ChatSession.ChatHistoryDirection direction, int take);
    Task<RequestResult> SetMessagePinned(int chatId, int messageId, bool pinned);

    Task<RequestResult> SetMessageReaction(int chatId, int messageId, string emoji, bool isAdded);

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
    /// Who of the other members has read an own message of a direct chat or a group, and when.
    /// </summary>
    Task<RequestResult<IReadOnlyList<ChatMessageReader>>> RequestChatMessageReaders(int chatId, int messageId);

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
