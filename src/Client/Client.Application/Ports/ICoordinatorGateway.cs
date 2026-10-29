using Shared.Permissions;
using Shared.Enums;
using Shared.Enums.User;
using Client.Core.Chat;
using Client.Core.Coordinator;
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
    
    // Requests: each fails with a RequestFailedException when the server answers with an error, and with a
    // TimeoutException when no answer comes
    /// <param name="password">The password of the server, not of the user.</param>
    /// <param name="sessionToken">
    /// The token of the device's session; logs in without the second stage while the server still knows it, and is
    /// otherwise passed over for <paramref name="userIdentity"/>.
    /// </param>
    /// <returns>
    /// <see cref="Shared.Enums.Auth.AuthResult.SecondStageRequired"/> with the challenge set when the user has a
    /// password, one-time codes or passkeys; <see cref="FinishLoginAsync"/> completes the login then.
    /// </returns>
    Task<AuthenticationResult> AuthenticateAsync(
        string? password, string? userIdentity, string? sessionToken);

    /// <summary>
    /// The second login stage: the password and the one-time code of the user, each when the challenge asks for it.
    /// A wrong answer may be tried again until the server closes the connection.
    /// </summary>
    Task<AuthenticationResult> FinishLoginAsync(string? password, string? twoFactorCode);

    // The login of the current user: each change answers with what the user has now
    Task<AccountSecurity> GetAccountSecurity();

    /// <summary>The devices the current user is logged in from, the latest used first.</summary>
    Task<IReadOnlyList<AccountSession>> GetAccountSessions();

    /// <summary>
    /// Ends a session of the current user, or every one but this device's when <paramref name="sessionId"/> is null;
    /// answers with the sessions left.
    /// </summary>
    Task<IReadOnlyList<AccountSession>> EndAccountSession(Guid? sessionId);

    /// <summary>Gives the current user a new identity and ends every other session; answers with the identity.</summary>
    Task<string> ResetUserIdentity(AccountProof proof);

    /// <summary>Sets the password of the current user, or removes it when <paramref name="newPassword"/> is null.</summary>
    Task<AccountSecurity> SetAccountPassword(AccountProof proof,
        string? newPassword);

    /// <summary>Offers a new secret for an authenticator app; codes are on once <see cref="EnableTwoFactor"/> confirms it.</summary>
    Task<TwoFactorSetup> SetupTwoFactor(AccountProof proof);
    Task<AccountSecurity> EnableTwoFactor(string code);
    Task<AccountSecurity> DisableTwoFactor(AccountProof proof);
    Task<AccountSecurity> DeletePasskey(AccountProof proof, int passkeyId);
    Task<CoordinatorSessionModel?> GetCoordinatorInfo();

    /// <summary>The stored settings of the coordinator and the SFU nodes they may name.</summary>
    Task<CoordinatorSettingsSnapshot> GetCoordinatorSettings();

    /// <summary>
    /// Replaces the settings of the coordinator, unless they were changed after <paramref name="expectedRevision"/>.
    /// Connected clients hear of a saved change through <see cref="CoordinatorReconfiguredEvent"/>.
    /// </summary>
    /// <returns>The settings as they were saved.</returns>
    Task<CoordinatorSettingsModel> UpdateCoordinatorSettings(CoordinatorSettingsUpdate update,
        long expectedRevision);
    /// <summary>The roles of the coordinator, lowest first, and what the current user may do.</summary>
    Task<IReadOnlyList<RoleModel>> GetRoles();

    Task<RoleModel> CreateRole(RoleChange change);

    Task<RoleModel> UpdateRole(int roleId, RoleChange change,
        long expectedRevision);

    Task DeleteRole(int roleId);

    /// <param name="roleIds">Every role but the everyone role, lowest first.</param>
    Task<IReadOnlyList<RoleModel>> ReorderRoles(IReadOnlyList<int> roleIds);

    /// <returns>The roles the user has afterwards.</returns>
    Task<IReadOnlyList<int>> SetUserRole(int userId, int roleId, bool has);

    Task<UserModel?> GetUserInfo(int userId);
    /// <summary>The users the server knows, a deleted one missing; null when the server refused.</summary>
    Task<Dictionary<int, UserModel>?> GetUsersInfo(List<int> userId);
    /// <summary>Users whose name matches the query, the best matches first; empty on a failure.</summary>
    Task<List<UserModel>> SearchUsers(string query, int limit);
    /// <summary>Every user of the coordinator but the system one, by name.</summary>
    Task<IReadOnlyList<UserModel>> GetAllUsers();
    /// <summary>Renames the given user, or the current one when <paramref name="userId"/> is null.</summary>
    Task<UserModel> ChangeUsername(string username, int? userId = null);

    /// <summary>
    /// Puts a finished avatar upload on the profile of the user, or takes the avatar off when
    /// <paramref name="uploadId"/> is null. The user is the current one when <paramref name="userId"/> is null.
    /// </summary>
    Task<UserModel> SetAvatar(string? uploadId, int? userId = null);

    /// <summary>Sets the status of the current user.</summary>
    Task<UserModel> SetStatus(UserStatus status);

    /// <summary>Bans, freezes or reinstates another user.</summary>
    /// <returns>The user as the server has them now, when the state was set.</returns>
    Task<UserModel> SetUserState(int userId, UserState state);
    Task DeleteUser(int userId);
    Task<string?> RegisterPushToken(string token, PushPlatform platform, PushKey key);
    Task UnregisterPushToken(string token);
    Task<SessionModel> GetSessionInfo(int sessionId);
    Task<(IDictionary<int, SessionModel> Sessions, SessionLayout Layout)> GetAllSessions();
    Task CreateSession(NewSessionModel newSession);

    /// <summary>
    /// Changes of the session layout. The new layout itself comes to every client, this one too,
    /// through the layout notification.
    /// </summary>
    Task<int> CreateSessionCategory(string name);
    Task RenameSessionCategory(int categoryId, string name);
    Task DeleteSessionCategory(int categoryId);
    Task MoveSession(int sessionId, int? categoryId, int index);
    Task MoveSessionCategory(int categoryId, int index);

    Task<Dictionary<int, PeerModel>> GetAllHybridSessionPeers(
        int sessionId);
    Task ConnectHybridSessionTransport(int sessionId,
        DtlsParameters dtlsParameters);

    Task<TransportParameters> JoinHybridSession(int sessionId,
        string clientEpoch);
    Task<OutboundTrack> StartVideoShareInHybridSession(int sessionId);
    Task StopVideoShareInHybridSession(int sessionId);
    Task<OutboundTrack> StartVoiceShareInHybridSession(int sessionId);
    Task StopVoiceShareInHybridSession(int sessionId);
    /// <param name="withAudio">Also ask for a producer of the sound of the shared application or system.</param>
    Task<ScreenShareOutbounds> StartScreenShareInHybridSession(int sessionId,
        bool withAudio);
    Task StopScreenShareInHybridSession(int sessionId);
    Task<ScreenShareInbounds?> SetScreenShareWatchInHybridSession(int sessionId,
        int userId, bool isWatching);
    Task ResumeInboundsInHybridSession(int sessionId, IReadOnlyList<string> inboundIds);
    Task SetMicrophoneMuteInHybridSession(int sessionId, bool isMuted);
    Task SetSoundMuteInHybridSession(int sessionId, bool isMuted);
    Task SetAfkInHybridSession(int sessionId, bool isAfk, string? reason);
    Task PokeInHybridSession(int sessionId, int userId, string? message);
    Task DeleteSession(int sessionId);

    /// <summary>
    /// The messages come as the server sent them, so <see cref="ChatMessageModel.IsMine"/> is not set.
    /// </summary>
    Task<ChatHistoryPage> RequestChatHistoryPage(int sessionId,
        int? beforeMessageId,
        int take);

    /// <inheritdoc cref="RequestChatHistoryPage"/>
    Task<ChatChanges> RequestChatChanges(int sessionId,
        long afterRevision,
        int take);
    Task<ChatMessageModel> RequestChatMessage(int sessionId,
        int messageId);
    Task SendChatMessage(int sessionId,
        NewChatMessage newMessage);
    Task DeleteChatMessage(int sessionId,
        int messageId);
    Task EditChatMessage(int sessionId,
        int messageId,
        string text);
    Task<IReadOnlyList<ChatReadState>> RequestChatReadStates();
    Task<ChatReadState> MarkChatRead(int sessionId,
        int messageId);
    Task LeaveSession(int sessionId);
    Task KickFromHybridSession(int sessionId, int userId);
    Task ApproveInHybridSession(int sessionId, int userId);
    Task RejectInHybridSession(int sessionId, int userId);
    Task ChangeHybridSessionSettings(int sessionId, string name, int capacity,
        bool isHostMustApprove, MediaSettings media);
    Task ChangeSessionPermissions(int sessionId,
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
