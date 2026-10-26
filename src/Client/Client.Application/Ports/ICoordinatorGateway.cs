using Shared.Permissions;
using Shared.Enums;
using Shared.Enums.Coordinator;
using Shared.Enums.User;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Core.Events;
using Client.Core.Push;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;
using Client.Core.Users;

namespace Client.Application.Ports;

/// <summary>
/// Connection to one coordinator. Speaks the wire protocol and hands out client models only.
/// </summary>
public interface ICoordinatorGateway : IDisposable
{
    /// <summary>
    /// Raised for everything the coordinator reports on its own. Raised on the transport thread.
    /// </summary>
    event Action<CoordinatorEvent>? EventReceived;

    /// <summary>
    /// Raised once when an open connection breaks: the socket dropped, the server closed it or the
    /// heartbeats stopped. Requests still waiting fail with a <see cref="TimeoutException"/>.
    /// Not raised for a disconnect asked for through <see cref="DisconnectWebSocketAsync"/>.
    /// </summary>
    event Action<string>? ConnectionLost;
    
    // Init methods
    /// <param name="trustedFingerprint">
    /// SHA-256 of a certificate the user trusted for this address. Accepted even when the system
    /// cannot verify it; any other unverified certificate fails the connection with
    /// <see cref="TransportConnectResult.RejectedCertificate"/> set.
    /// </param>
    /// <param name="cancellationToken">Gives up a connection that is still being opened.</param>
    /// <exception cref="OperationCanceledException">Canceled before the connection was open.</exception>
    Task<TransportConnectResult> ConnectWebSocketAsync(
        string address, 
        int port,
        string? trustedFingerprint,
        CancellationToken cancellationToken = default);
    Task DisconnectWebSocketAsync();

    /// <summary>
    /// Asks the server for an answer out of turn, when there is a reason to doubt the connection
    /// and the heartbeat would take too long to notice.
    /// </summary>
    /// <returns>False when no answer came in <paramref name="timeout"/> or the connection is gone.</returns>
    Task<bool> CheckConnectionAsync(TimeSpan timeout);
    
    // Requests 
    /// <param name="password">The password of the server, not of the user.</param>
    /// <returns>
    /// <see cref="Shared.Enums.Auth.AuthResult.SecondStageRequeried"/> with the challenge set when the user has a
    /// password, one-time codes or passkeys; <see cref="FinishLoginAsync"/> completes the login then.
    /// </returns>
    Task<AuthenticationResult> AuthenticateAsync(
        string? password, string? userIdentity);

    /// <summary>
    /// The second login stage: the password and the one-time code of the user, each when the challenge asks for it.
    /// A wrong answer may be tried again until the server closes the connection.
    /// </summary>
    Task<AuthenticationResult> FinishLoginAsync(string? password, string? twoFactorCode);

    // The login of the current user: each change answers with what the user has now
    Task<(UserSecurityResult Result, AccountSecurity? Security)> GetAccountSecurity();

    /// <summary>Sets the password of the current user, or removes it when <paramref name="newPassword"/> is null.</summary>
    Task<(UserSecurityResult Result, AccountSecurity? Security)> SetAccountPassword(AccountProof proof,
        string? newPassword);

    /// <summary>Offers a new secret for an authenticator app; codes are on once <see cref="EnableTwoFactor"/> confirms it.</summary>
    Task<(UserSecurityResult Result, TwoFactorSetup? Setup)> SetupTwoFactor(AccountProof proof);
    Task<(UserSecurityResult Result, AccountSecurity? Security)> EnableTwoFactor(string code);
    Task<(UserSecurityResult Result, AccountSecurity? Security)> DisableTwoFactor(AccountProof proof);
    Task<(UserSecurityResult Result, AccountSecurity? Security)> DeletePasskey(AccountProof proof, int passkeyId);
    Task<CoordinatorSessionModel?> GetCoordinatorInfo();

    /// <summary>The stored settings of the coordinator and the SFU nodes they may name.</summary>
    Task<(CoordinatorSettingsResult Result, CoordinatorSettingsSnapshot? Snapshot)> GetCoordinatorSettings();

    /// <summary>
    /// Replaces the settings of the coordinator, unless they were changed after <paramref name="expectedRevision"/>.
    /// Connected clients hear of a saved change through <see cref="CoordinatorReconfiguredEvent"/>.
    /// </summary>
    Task<CoordinatorSettingsUpdateOutcome> UpdateCoordinatorSettings(CoordinatorSettingsUpdate update,
        long expectedRevision);
    /// <summary>The roles of the coordinator, lowest first, and what the current user may do.</summary>
    Task<(CoordinatorRoleResult Result, IReadOnlyList<RoleModel>? Roles)> GetRoles();

    Task<(CoordinatorRoleResult Result, RoleModel? Role)> CreateRole(RoleChange change);

    /// <summary>On <see cref="CoordinatorRoleResult.Conflict"/> the role is the one saved meanwhile.</summary>
    Task<(CoordinatorRoleResult Result, RoleModel? Role)> UpdateRole(int roleId, RoleChange change,
        long expectedRevision);

    Task<CoordinatorRoleResult> DeleteRole(int roleId);

    /// <param name="roleIds">Every role but the everyone role, lowest first.</param>
    Task<(CoordinatorRoleResult Result, IReadOnlyList<RoleModel>? Roles)> ReorderRoles(IReadOnlyList<int> roleIds);

    /// <returns>The roles the user has afterwards.</returns>
    Task<(CoordinatorRoleResult Result, IReadOnlyList<int>? RoleIds)> SetUserRole(int userId, int roleId, bool has);

    Task<UserModel?> GetUserInfo(int userId);
    /// <summary>The users the server knows, a deleted one missing; null when the server refused.</summary>
    Task<Dictionary<int, UserModel>?> GetUsersInfo(List<int> userId);
    /// <summary>Users whose name matches the query, the best matches first; empty on a failure.</summary>
    Task<List<UserModel>> SearchUsers(string query, int limit);
    /// <summary>Every user of the coordinator but the system one, by name.</summary>
    Task<(UsersRequestResult Result, IReadOnlyList<UserModel>? Users)> GetAllUsers();
    /// <summary>Renames the given user, or the current one when <paramref name="userId"/> is null.</summary>
    Task<(ChangeUsernameResult Result, UserModel? User)> ChangeUsername(string username, int? userId = null);

    /// <summary>
    /// Puts a finished avatar upload on the profile of the user, or takes the avatar off when
    /// <paramref name="uploadId"/> is null. The user is the current one when <paramref name="userId"/> is null.
    /// </summary>
    Task<(SetAvatarResult Result, UserModel? User)> SetAvatar(string? uploadId, int? userId = null);

    /// <summary>Sets the status of the current user.</summary>
    Task<(SetUserStatusResult Result, UserModel? User)> SetStatus(UserStatus status);

    /// <summary>Bans, freezes or reinstates another user.</summary>
    /// <returns>The user as the server has them now, when the state was set.</returns>
    Task<(ModerateUserResult Result, UserModel? User)> SetUserState(int userId, UserState state);
    Task<ModerateUserResult> DeleteUser(int userId);
    Task<(PushTokenResult Result, string? ReplyToken)> RegisterPushToken(string token, PushPlatform platform, PushKey key);
    Task<PushTokenResult> UnregisterPushToken(string token);
    Task<(SessionRequestResult Result, SessionModel? Session)> GetSessionInfo(int sessionId);
    Task<(SessionRequestResult Result, IDictionary<int, SessionModel>? Sessions, SessionLayout? Layout)> GetAllSessions();
    Task<CreateSessionResult> CreateSession(NewSessionModel newSession);

    /// <summary>
    /// Changes of the session layout. The new layout itself comes to every client, this one too,
    /// through the layout notification.
    /// </summary>
    Task<(SessionLayoutResult Result, int? CategoryId)> CreateSessionCategory(string name);
    Task<SessionLayoutResult> RenameSessionCategory(int categoryId, string name);
    Task<SessionLayoutResult> DeleteSessionCategory(int categoryId);
    Task<SessionLayoutResult> MoveSession(int sessionId, int? categoryId, int index);
    Task<SessionLayoutResult> MoveSessionCategory(int categoryId, int index);

    Task<(PeerListRequestResult Result, Dictionary<int, PeerModel>? Peers)> GetAllHybridSessionPeers(
        int sessionId);
    Task<HybridSessionConnectTransportResult> ConnectHybridSessionTransport(int sessionId,
        DtlsParameters dtlsParameters);

    Task<(HybridSessionJoinResult Result, TransportParameters? Transport)> JoinHybridSession(int sessionId,
        string clientEpoch);
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
    Task<ResumeInboundsResult> ResumeInboundsInHybridSession(int sessionId, IReadOnlyList<string> inboundIds);
    Task<SetMicrophoneMuteResult> SetMicrophoneMuteInHybridSession(int sessionId, bool isMuted);
    Task<SetSoundMuteResult> SetSoundMuteInHybridSession(int sessionId, bool isMuted);
    Task<SetAfkResult> SetAfkInHybridSession(int sessionId, bool isAfk, string? reason);
    Task<PokeResult> PokeInHybridSession(int sessionId, int userId, string? message);
    Task<DeleteSessionResult> DeleteSession(int sessionId);

    /// <summary>
    /// The messages come as the server sent them, so <see cref="ChatMessageModel.IsMine"/> is not set.
    /// </summary>
    Task<(FetchMessagesResult Result, ChatHistoryPage? Page)> RequestChatHistoryPage(int sessionId,
        int? beforeMessageId,
        int take);

    /// <inheritdoc cref="RequestChatHistoryPage"/>
    Task<(FetchMessagesResult Result, ChatChanges? Changes)> RequestChatChanges(int sessionId,
        long afterRevision,
        int take);
    Task<(FetchMessageResult Result, ChatMessageModel? Message)> RequestChatMessage(int sessionId,
        int messageId);
    Task<AddMessageResult> SendChatMessage(int sessionId,
        NewChatMessage newMessage);
    Task<DeleteMessageResult> DeleteChatMessage(int sessionId,
        int messageId);
    Task<EditMessageResult> EditChatMessage(int sessionId,
        int messageId,
        string text);
    Task<(ChatReadResult Result, IReadOnlyList<ChatReadState>? States)> RequestChatReadStates();
    Task<(ChatReadResult Result, ChatReadState? State)> MarkChatRead(int sessionId,
        int messageId);
    Task<LeaveSessionResult> LeaveSession(int sessionId);
    Task<LeaveSessionResult> KickFromHybridSession(int sessionId, int userId);
    Task<ApproveUserSessionResult> ApproveInHybridSession(int sessionId, int userId);
    Task<RejectUserSessionResult> RejectInHybridSession(int sessionId, int userId);
    Task<ChangeSessionSettingsResult> ChangeHybridSessionSettings(int sessionId, string name, int capacity,
        bool isHostMustApprove, MediaSettings media);
    Task<ChangeSessionPermissionsResult> ChangeSessionPermissions(int sessionId,
        IReadOnlyList<PermissionOverride> overrides);

    /// <summary>
    /// Puts a file on the server over HTTP, in parts, and continues after a break in the connection.
    /// Failures come back as a result, never as an exception.
    /// </summary>
    /// <param name="progress">Reports how many bytes the server holds.</param>
    Task<MediaUploadOutcome> UploadMedia(MediaUpload upload, IProgress<long>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes an image of a message, or its reduced copy, into <paramref name="destination"/>.
    /// Failures come back as a result, never as an exception.
    /// </summary>
    /// <param name="progress">Reports how many bytes were received.</param>
    Task<MediaTransferResult> DownloadChatImage(int imageId, bool preview, Stream destination,
        IProgress<long>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a file of a message into <paramref name="destination"/>.
    /// Failures come back as a result, never as an exception.
    /// </summary>
    /// <param name="progress">Reports how many bytes were received.</param>
    Task<MediaTransferResult> DownloadChatFile(int fileId, Stream destination, IProgress<long>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes the avatar with the id from <see cref="Client.Core.Users.AvatarAddress.IdOf"/> into
    /// <paramref name="destination"/>. Failures come back as a result, never as an exception.
    /// </summary>
    Task<MediaTransferResult> DownloadAvatar(string avatarId, Stream destination, CancellationToken cancellationToken);
}
