using Shared.Enums;
using Shared.Enums.Coordinator;
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
    public event Action<RequestRejectedException>? RequestRejected;

    public Task<(SessionRequestResult Result, IDictionary<int, SessionModel>? Sessions, SessionLayout? Layout)>
        GetAllSessions() =>
        SendAsync(_gateway.GetAllSessions,
            (SessionRequestResult.Rejected, (IDictionary<int, SessionModel>?)null, (SessionLayout?)null),
            (SessionRequestResult.TimedOut, (IDictionary<int, SessionModel>?)null, (SessionLayout?)null));

    public Task<(SessionRequestResult Result, SessionModel? Session)> GetSessionInfo(int sessionId) =>
        SendAsync(() => _gateway.GetSessionInfo(sessionId), SessionRequestResult.Rejected, SessionRequestResult.TimedOut);

    public Task<CreateSessionResult> CreateSession(NewSessionModel session) =>
        ValidateSession(session)
            ? SendAsync(() => _gateway.CreateSession(session), CreateSessionResult.Rejected, CreateSessionResult.TimedOut)
            : Task.FromResult(CreateSessionResult.UnexpectedParameters);

    public bool ValidateSession(NewSessionModel newSession)
    {
        // Mirrors the server's checks on CreateSessionModel, so the user gets an answer without a round trip
        var name = newSession.Name;
        return !string.IsNullOrWhiteSpace(name)
               && name.Length <= Limits.SessionNameMaxLength
               && !name.Any(char.IsControl)
               && newSession.Capacity is >= Limits.SessionCapacityMin and <= Limits.SessionCapacityMax;
    }

    public Task<DeleteSessionResult> DeleteSession(int sessionId) =>
        SendAsync(() => _gateway.DeleteSession(sessionId), DeleteSessionResult.Rejected, DeleteSessionResult.TimedOut);

    public Task<(SessionLayoutResult Result, int? CategoryId)> CreateSessionCategory(string name) =>
        ValidateCategoryName(name)
            ? SendAsync(() => _gateway.CreateSessionCategory(name),
                (SessionLayoutResult.Rejected, (int?)null), (SessionLayoutResult.TimedOut, (int?)null))
            : Task.FromResult((SessionLayoutResult.InternalError, (int?)null));

    public Task<SessionLayoutResult> RenameSessionCategory(int categoryId, string name) =>
        ValidateCategoryName(name)
            ? SendAsync(() => _gateway.RenameSessionCategory(categoryId, name),
                SessionLayoutResult.Rejected, SessionLayoutResult.TimedOut)
            : Task.FromResult(SessionLayoutResult.InternalError);

    /// <summary>
    /// Mirrors the server's checks on a category name, so a bad one is not sent at all.
    /// </summary>
    public static bool ValidateCategoryName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= Limits.SessionCategoryNameMaxLength
        && !name.Any(char.IsControl);

    public Task<SessionLayoutResult> DeleteSessionCategory(int categoryId) =>
        SendAsync(() => _gateway.DeleteSessionCategory(categoryId),
            SessionLayoutResult.Rejected, SessionLayoutResult.TimedOut);

    public Task<SessionLayoutResult> MoveSession(int sessionId, int? categoryId, int index) =>
        SendAsync(() => _gateway.MoveSession(sessionId, categoryId, index),
            SessionLayoutResult.Rejected, SessionLayoutResult.TimedOut);

    public Task<SessionLayoutResult> MoveSessionCategory(int categoryId, int index) =>
        SendAsync(() => _gateway.MoveSessionCategory(categoryId, index),
            SessionLayoutResult.Rejected, SessionLayoutResult.TimedOut);

    public Task<LeaveSessionResult> LeaveSession(int sessionId) =>
        SendAsync(() => _gateway.LeaveSession(sessionId), LeaveSessionResult.Rejected, LeaveSessionResult.TimedOut);

    public Task<LeaveSessionResult> KickFromHybridSession(int sessionId, int userId) =>
        userId == _currentUser?.Id
            ? Task.FromResult(LeaveSessionResult.CannotKickSelf)
            : SendAsync(() => _gateway.KickFromHybridSession(sessionId, userId),
                LeaveSessionResult.Rejected, LeaveSessionResult.TimedOut);

    public Task<ChangeSessionSettingsResult> ChangeHybridSessionSettings(int sessionId, string name, int capacity,
        bool isHostMustApprove) =>
        SendAsync(() => _gateway.ChangeHybridSessionSettings(sessionId, name.Trim(), capacity, isHostMustApprove),
            ChangeSessionSettingsResult.Rejected, ChangeSessionSettingsResult.TimedOut);

    public Task<(HybridSessionJoinResult Result, TransportParameters? Transport)> JoinHybridSession(int sessionId,
        string clientEpoch) =>
        SendAsync(() => _gateway.JoinHybridSession(sessionId, clientEpoch),
            HybridSessionJoinResult.Rejected, HybridSessionJoinResult.TimedOut);

    public Task<HybridSessionConnectTransportResult> ConnectHybridSessionTransport(int sessionId,
        DtlsParameters dtlsParameters) =>
        SendAsync(() => _gateway.ConnectHybridSessionTransport(sessionId, dtlsParameters),
            HybridSessionConnectTransportResult.Rejected, HybridSessionConnectTransportResult.InternalError);

    public Task<(PeerListRequestResult Result, Dictionary<int, PeerModel>? Peers)> GetAllHybridSessionPeers(
        int sessionId) =>
        SendAsync(() => _gateway.GetAllHybridSessionPeers(sessionId),
            PeerListRequestResult.Rejected, PeerListRequestResult.InternalError);

    public Task<(StartVideoShareResult Result, OutboundTrack? Outbound)> StartVideoShareInHybridSession(int sessionId) =>
        SendAsync(() => _gateway.StartVideoShareInHybridSession(sessionId),
            StartVideoShareResult.Rejected, StartVideoShareResult.TimedOut);

    public Task<StopVideoShareResult> StopVideoShareInHybridSession(int sessionId) =>
        SendAsync(() => _gateway.StopVideoShareInHybridSession(sessionId),
            StopVideoShareResult.Rejected, StopVideoShareResult.TimedOut);

    public Task<(StartVoiceShareResult Result, OutboundTrack? Outbound)> StartVoiceShareInHybridSession(int sessionId) =>
        SendAsync(() => _gateway.StartVoiceShareInHybridSession(sessionId),
            StartVoiceShareResult.Rejected, StartVoiceShareResult.TimedOut);

    public Task<StopVoiceShareResult> StopVoiceShareInHybridSession(int sessionId) =>
        SendAsync(() => _gateway.StopVoiceShareInHybridSession(sessionId),
            StopVoiceShareResult.Rejected, StopVoiceShareResult.TimedOut);

    public Task<(StartScreenShareResult Result, ScreenShareOutbounds? Outbounds)> StartScreenShareInHybridSession(
        int sessionId, bool withAudio) =>
        SendAsync(() => _gateway.StartScreenShareInHybridSession(sessionId, withAudio),
            StartScreenShareResult.Rejected, StartScreenShareResult.TimedOut);

    public Task<StopScreenShareResult> StopScreenShareInHybridSession(int sessionId) =>
        SendAsync(() => _gateway.StopScreenShareInHybridSession(sessionId),
            StopScreenShareResult.Rejected, StopScreenShareResult.TimedOut);

    public Task<(SetScreenShareWatchResult Result, ScreenShareInbounds? Inbounds)> SetScreenShareWatchInHybridSession(
        int sessionId, int userId, bool isWatching) =>
        SendAsync(() => _gateway.SetScreenShareWatchInHybridSession(sessionId, userId, isWatching),
            SetScreenShareWatchResult.Rejected, SetScreenShareWatchResult.TimedOut);

    public Task<ResumeInboundsResult> ResumeInboundsInHybridSession(int sessionId,
        IReadOnlyList<string> inboundIds) =>
        SendAsync(() => _gateway.ResumeInboundsInHybridSession(sessionId, inboundIds),
            ResumeInboundsResult.Rejected, ResumeInboundsResult.TimedOut);

    public Task<SetMicrophoneMuteResult> SetMicrophoneMuteInHybridSession(int sessionId, bool isMuted) =>
        SendAsync(() => _gateway.SetMicrophoneMuteInHybridSession(sessionId, isMuted),
            SetMicrophoneMuteResult.Rejected, SetMicrophoneMuteResult.TimedOut);

    public Task<SetSoundMuteResult> SetSoundMuteInHybridSession(int sessionId, bool isMuted) =>
        SendAsync(() => _gateway.SetSoundMuteInHybridSession(sessionId, isMuted),
            SetSoundMuteResult.Rejected, SetSoundMuteResult.TimedOut);

    public async Task<(FetchMessagesResult Result, ChatHistoryPage? Page)> RequestChatHistoryPage(
        int sessionId, int? beforeMessageId, int take)
    {
        var (result, page) = await SendAsync(() => _gateway.RequestChatHistoryPage(sessionId, beforeMessageId, take),
            FetchMessagesResult.Rejected, FetchMessagesResult.TimedOut);

        MarkMine(page?.Messages);
        return (result, page);
    }

    public async Task<(FetchMessagesResult Result, ChatChanges? Changes)> RequestChatChanges(
        int sessionId, long afterRevision, int take)
    {
        var (result, changes) = await SendAsync(() => _gateway.RequestChatChanges(sessionId, afterRevision, take),
            FetchMessagesResult.Rejected, FetchMessagesResult.TimedOut);

        MarkMine(changes?.Messages);
        return (result, changes);
    }

    private void MarkMine(IReadOnlyList<ChatMessageModel>? messages)
    {
        if (messages == null)
            return;

        var currentUserId = GetCurrentUser().Id;

        foreach (var message in messages)
            message.IsMine = message.SenderUserId == currentUserId;
    }

    public async Task<(FetchMessageResult Result, ChatMessageModel? Message)> RequestChatMessage(int sessionId,
        int messageId)
    {
        var (result, message) = await SendAsync(() => _gateway.RequestChatMessage(sessionId, messageId),
            FetchMessageResult.Rejected, FetchMessageResult.TimedOut);

        if (message != null)
            message.IsMine = message.SenderUserId == GetCurrentUser().Id;

        return (result, message);
    }

    public Task<AddMessageResult> SendChatMessage(NewChatMessage message) =>
        SendAsync(() => _gateway.SendChatMessage(message.ChatId, message),
            AddMessageResult.Rejected, AddMessageResult.TimedOut);

    public Task<DeleteMessageResult> DeleteChatMessage(int sessionId, int messageId) =>
        SendAsync(() => _gateway.DeleteChatMessage(sessionId, messageId),
            DeleteMessageResult.Rejected, DeleteMessageResult.TimedOut);

    public Task<EditMessageResult> EditChatMessage(int sessionId, int messageId, ChatTextContent text) =>
        SendAsync(() => _gateway.EditChatMessage(sessionId, messageId, text.Text),
            EditMessageResult.Rejected, EditMessageResult.TimedOut);

    public Task<(ChatReadResult Result, IReadOnlyList<ChatReadState>? States)> RequestChatReadStates() =>
        SendAsync(_gateway.RequestChatReadStates, ChatReadResult.Rejected, ChatReadResult.TimedOut);

    public Task<(ChatReadResult Result, ChatReadState? State)> MarkChatRead(int chatId, int messageId) =>
        SendAsync(() => _gateway.MarkChatRead(chatId, messageId), ChatReadResult.Rejected, ChatReadResult.TimedOut);

    /// <summary>
    /// Turns the two ways a request fails without an answer into its result: a refusal is reported
    /// through <see cref="RequestRejected"/>, a timeout or a lost connection is just a result.
    /// </summary>
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

    public Task<(PushTokenResult Result, string? ReplyToken)> RegisterPushToken(string token, PushPlatform platform,
        PushKey key) =>
        SendAsync(() => _gateway.RegisterPushToken(token, platform, key),
            PushTokenResult.Rejected, PushTokenResult.TimedOut);

    public Task<PushTokenResult> UnregisterPushToken(string token) =>
        SendAsync(() => _gateway.UnregisterPushToken(token), PushTokenResult.Rejected, PushTokenResult.TimedOut);

    public Task<(CoordinatorSettingsResult Result, CoordinatorSettingsSnapshot? Snapshot)> GetCoordinatorSettings() =>
        SendAsync(_gateway.GetCoordinatorSettings, CoordinatorSettingsResult.Rejected, CoordinatorSettingsResult.TimedOut);

    public Task<CoordinatorSettingsUpdateOutcome> UpdateCoordinatorSettings(CoordinatorSettingsUpdate update,
        long expectedRevision) =>
        SendAsync(() => _gateway.UpdateCoordinatorSettings(update, expectedRevision),
            CoordinatorSettingsUpdateOutcome.Of(CoordinatorSettingsResult.Rejected),
            CoordinatorSettingsUpdateOutcome.Of(CoordinatorSettingsResult.TimedOut));

    private async Task<TResult> SendAsync<TResult>(Func<Task<TResult>> request, TResult rejected, TResult timedOut)
    {
        try
        {
            return await request();
        }
        catch (RequestRejectedException exception)
        {
            return OnRequestRejected(exception, rejected);
        }
        catch (TimeoutException)
        {
            return timedOut;
        }
    }

    /// <summary>
    /// The same for requests that also return data, which is null when the request fails here.
    /// </summary>
    private Task<(TResult Result, TData? Data)> SendAsync<TResult, TData>(
        Func<Task<(TResult Result, TData? Data)>> request, TResult rejected, TResult timedOut)
        where TData : class =>
        SendAsync(request, (rejected, (TData?)null), (timedOut, (TData?)null));

    private T OnRequestRejected<T>(RequestRejectedException exception, T result)
    {
        LogRequestRejected(LogName, exception.Message);
        RequestRejected?.Invoke(exception);
        return result;
    }
}
