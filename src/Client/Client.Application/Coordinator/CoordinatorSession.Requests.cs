using Shared.Permissions;
using Shared.Enums;
using Shared.Validation;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Core.Push;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;
using Client.Application.Ports;

namespace Client.Application.Coordinator;

public partial class CoordinatorSession
{
    public event Action<RequestFailedException>? RequestRejected;

    public Task<RequestResult<(IDictionary<int, SessionModel> Sessions, SessionLayout Layout)>> GetAllSessions() =>
        SendAsync(_gateway.GetAllSessions);

    public Task<RequestResult<SessionModel>> GetSessionInfo(int sessionId) =>
        SendAsync(() => _gateway.GetSessionInfo(sessionId));

    public Task<RequestResult> CreateSession(NewSessionModel session) =>
        ValidateSession(session)
            ? SendAsync(() => _gateway.CreateSession(session))
            : Task.FromResult<RequestResult>(ErrorCode.InvalidValue);

    public bool ValidateSession(NewSessionModel newSession)
    {
        // Mirrors the server's checks on CreateSessionModel, so the user gets an answer without a round trip
        var name = newSession.Name;
        return !string.IsNullOrWhiteSpace(name)
               && name.Length <= Limits.SessionNameMaxLength
               && !name.Any(char.IsControl)
               && newSession.Capacity is >= Limits.SessionCapacityMin and <= Limits.SessionCapacityMax;
    }

    public Task<RequestResult> DeleteSession(int sessionId) =>
        SendAsync(() => _gateway.DeleteSession(sessionId));

    public Task<RequestResult<int>> CreateSessionCategory(string name) =>
        ValidateCategoryName(name)
            ? SendAsync(() => _gateway.CreateSessionCategory(name))
            : Task.FromResult<RequestResult<int>>(ErrorCode.InvalidValue);

    public Task<RequestResult> RenameSessionCategory(int categoryId, string name) =>
        ValidateCategoryName(name)
            ? SendAsync(() => _gateway.RenameSessionCategory(categoryId, name))
            : Task.FromResult<RequestResult>(ErrorCode.InvalidValue);

    /// <summary>
    /// Mirrors the server's checks on a category name, so a bad one is not sent at all.
    /// </summary>
    public static bool ValidateCategoryName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= Limits.SessionCategoryNameMaxLength
        && !name.Any(char.IsControl);

    public Task<RequestResult> DeleteSessionCategory(int categoryId) =>
        SendAsync(() => _gateway.DeleteSessionCategory(categoryId));

    public Task<RequestResult> MoveSession(int sessionId, int? categoryId, int index) =>
        SendAsync(() => _gateway.MoveSession(sessionId, categoryId, index));

    public Task<RequestResult> MoveSessionCategory(int categoryId, int index) =>
        SendAsync(() => _gateway.MoveSessionCategory(categoryId, index));

    public Task<RequestResult> LeaveSession(int sessionId) =>
        SendAsync(() => _gateway.LeaveSession(sessionId));

    public Task<RequestResult> KickFromHybridSession(int sessionId, int userId) =>
        userId == _currentUser?.Id
            ? Task.FromResult<RequestResult>(ErrorCode.CannotTargetSelf)
            : SendAsync(() => _gateway.KickFromHybridSession(sessionId, userId));

    public Task<RequestResult> ApproveInHybridSession(int sessionId, int userId) =>
        SendAsync(() => _gateway.ApproveInHybridSession(sessionId, userId));

    public Task<RequestResult> RejectInHybridSession(int sessionId, int userId) =>
        SendAsync(() => _gateway.RejectInHybridSession(sessionId, userId));

    public Task<RequestResult> ChangeHybridSessionSettings(int sessionId, string name, int capacity,
        bool isHostMustApprove, MediaSettings media) =>
        SendAsync(() => _gateway.ChangeHybridSessionSettings(sessionId, name.Trim(), capacity, isHostMustApprove, media));

    public Task<RequestResult> ChangeSessionPermissions(int sessionId,
        IReadOnlyList<PermissionOverride> overrides) =>
        SendAsync(() => _gateway.ChangeSessionPermissions(sessionId, overrides));

    public Task<RequestResult<TransportParameters>> JoinHybridSession(int sessionId, string clientEpoch) =>
        SendAsync(() => _gateway.JoinHybridSession(sessionId, clientEpoch));

    public Task<RequestResult> ConnectHybridSessionTransport(int sessionId,
        DtlsParameters dtlsParameters) =>
        SendAsync(() => _gateway.ConnectHybridSessionTransport(sessionId, dtlsParameters));

    public Task<RequestResult<Dictionary<int, PeerModel>>> GetAllHybridSessionPeers(int sessionId) =>
        SendAsync(() => _gateway.GetAllHybridSessionPeers(sessionId));

    public Task<RequestResult<OutboundTrack>> StartVideoShareInHybridSession(int sessionId) =>
        SendAsync(() => _gateway.StartVideoShareInHybridSession(sessionId));

    public Task<RequestResult> StopVideoShareInHybridSession(int sessionId) =>
        SendAsync(() => _gateway.StopVideoShareInHybridSession(sessionId));

    public Task<RequestResult<OutboundTrack>> StartVoiceShareInHybridSession(int sessionId) =>
        SendAsync(() => _gateway.StartVoiceShareInHybridSession(sessionId));

    public Task<RequestResult> StopVoiceShareInHybridSession(int sessionId) =>
        SendAsync(() => _gateway.StopVoiceShareInHybridSession(sessionId));

    public Task<RequestResult<ScreenShareOutbounds>> StartScreenShareInHybridSession(
        int sessionId, bool withAudio) =>
        SendAsync(() => _gateway.StartScreenShareInHybridSession(sessionId, withAudio));

    public Task<RequestResult> StopScreenShareInHybridSession(int sessionId) =>
        SendAsync(() => _gateway.StopScreenShareInHybridSession(sessionId));

    public Task<RequestResult<ScreenShareInbounds?>> SetScreenShareWatchInHybridSession(
        int sessionId, int userId, bool isWatching) =>
        SendAsync(() => _gateway.SetScreenShareWatchInHybridSession(sessionId, userId, isWatching));

    public Task<RequestResult> ResumeInboundsInHybridSession(int sessionId,
        IReadOnlyList<string> inboundIds) =>
        SendAsync(() => _gateway.ResumeInboundsInHybridSession(sessionId, inboundIds));

    public Task<RequestResult> SetMicrophoneMuteInHybridSession(int sessionId, bool isMuted) =>
        SendAsync(() => _gateway.SetMicrophoneMuteInHybridSession(sessionId, isMuted));

    public Task<RequestResult> SetSoundMuteInHybridSession(int sessionId, bool isMuted) =>
        SendAsync(() => _gateway.SetSoundMuteInHybridSession(sessionId, isMuted));

    public Task<RequestResult> SetAfkInHybridSession(int sessionId, bool isAfk, string? reason) =>
        SendAsync(() => _gateway.SetAfkInHybridSession(sessionId, isAfk, reason));

    public Task<RequestResult> PokeInHybridSession(int sessionId, int userId, string? message) =>
        SendAsync(() => _gateway.PokeInHybridSession(sessionId, userId, message));

    public async Task<RequestResult<ChatHistoryPage>> RequestChatHistoryPage(
        int sessionId, int? beforeMessageId, int take)
    {
        var result = await SendAsync(() => _gateway.RequestChatHistoryPage(sessionId, beforeMessageId, take));

        MarkMine(result.Value?.Messages);
        return result;
    }

    public async Task<RequestResult<ChatChanges>> RequestChatChanges(
        int sessionId, long afterRevision, int take)
    {
        var result = await SendAsync(() => _gateway.RequestChatChanges(sessionId, afterRevision, take));

        MarkMine(result.Value?.Messages);
        return result;
    }

    private void MarkMine(IReadOnlyList<ChatMessageModel>? messages)
    {
        if (messages == null)
            return;

        var currentUserId = GetCurrentUser().Id;

        foreach (var message in messages)
            message.IsMine = message.SenderUserId == currentUserId;
    }

    public async Task<RequestResult<ChatMessageModel>> RequestChatMessage(int sessionId,
        int messageId)
    {
        var result = await SendAsync(() => _gateway.RequestChatMessage(sessionId, messageId));

        if (result.IsSuccess)
            result.Value.IsMine = result.Value.SenderUserId == GetCurrentUser().Id;

        return result;
    }

    public Task<RequestResult> SendChatMessage(NewChatMessage message) =>
        SendAsync(() => _gateway.SendChatMessage(message.ChatId, message));

    public Task<RequestResult> DeleteChatMessage(int sessionId, int messageId) =>
        SendAsync(() => _gateway.DeleteChatMessage(sessionId, messageId));

    public Task<RequestResult> EditChatMessage(int sessionId, int messageId, ChatTextContent text) =>
        SendAsync(() => _gateway.EditChatMessage(sessionId, messageId, text.Text));

    public Task<RequestResult<IReadOnlyList<ChatReadState>>> RequestChatReadStates() =>
        SendAsync(_gateway.RequestChatReadStates);

    public Task<RequestResult<ChatReadState>> MarkChatRead(int chatId, int messageId) =>
        SendAsync(() => _gateway.MarkChatRead(chatId, messageId));

    public Task<MediaUploadOutcome> UploadMedia(MediaUpload upload, IProgress<long>? progress = null,
        CancellationToken cancellationToken = default) =>
        _gateway.UploadMedia(upload, progress, cancellationToken);

    public Task<MediaTransferResult> DownloadAvatar(string avatarId, Stream destination,
        CancellationToken cancellationToken = default) =>
        _gateway.DownloadAvatar(avatarId, destination, cancellationToken);

    public Task<MediaTransferResult> DownloadChatImage(int imageId, bool preview, Stream destination,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
        _gateway.DownloadChatImage(imageId, preview, destination, progress, cancellationToken);

    public Task<MediaTransferResult> DownloadChatFile(int fileId, Stream destination,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
        _gateway.DownloadChatFile(fileId, destination, progress, cancellationToken);

    public Task<RequestResult<string?>> RegisterPushToken(string token, PushPlatform platform,
        PushKey key) =>
        SendAsync(() => _gateway.RegisterPushToken(token, platform, key));

    public Task<RequestResult> UnregisterPushToken(string token) =>
        SendAsync(() => _gateway.UnregisterPushToken(token));

    public Task<RequestResult<CoordinatorSettingsSnapshot>> GetCoordinatorSettings() =>
        SendAsync(_gateway.GetCoordinatorSettings);

    public Task<RequestResult<CoordinatorSettingsModel>> UpdateCoordinatorSettings(CoordinatorSettingsUpdate update,
        long expectedRevision) =>
        SendAsync(() => _gateway.UpdateCoordinatorSettings(update, expectedRevision));

    /// <summary>
    /// Turns the ways a request fails into its result. The failures of the request as a whole that the user
    /// is told about (too many requests, missing permissions...) are reported through <see cref="RequestRejected"/>
    /// as well; a timeout or a lost connection is just a result.
    /// </summary>
    private async Task<RequestResult> SendAsync(Func<Task> request)
    {
        try
        {
            await request();
            return RequestResult.Success;
        }
        catch (RequestFailedException exception)
        {
            return OnRequestFailed(exception);
        }
        catch (TimeoutException)
        {
            return ErrorCode.TimedOut;
        }
    }

    /// <summary>
    /// The same for requests that return a value.
    /// </summary>
    private async Task<RequestResult<T>> SendAsync<T>(Func<Task<T>> request)
    {
        try
        {
            return await request();
        }
        catch (RequestFailedException exception)
        {
            return OnRequestFailed(exception);
        }
        catch (TimeoutException)
        {
            return ErrorCode.TimedOut;
        }
    }

    private RequestResult OnRequestFailed(RequestFailedException exception)
    {
        var result = new RequestResult(exception.Code, exception.Fields);

        if (result.IsRejected)
        {
            LogRequestRejected(LogName, exception.Message);
            RequestRejected?.Invoke(exception);
        }

        return result;
    }
}
