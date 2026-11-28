using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Application.Chat;
using Client.Application.Coordinator;
using Client.Application.Ports.Repositories;
using Client.Application.Tasks;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Enums;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>
/// The messages sent from this client that the server has not taken yet, for every coordinator. The composer hands a
/// message over and is free at once; the outbox compresses and uploads its files and sends it, one message of a chat
/// after another, so they arrive in the order they were sent. A message waits while the connection is down and goes
/// once it is back; what a new attempt may cure (a lost answer, uploads the server dropped, a changed compression
/// policy) is tried again by itself, anything else waits for the user. Every message is kept in the database until it
/// is sent or given up, so it still goes after a restart. Used on the UI thread.
/// </summary>
public sealed partial class ChatOutbox
{
    // A refusal that a new attempt may cure is tried again this often before the message waits for the user
    private const int MaxAutomaticRetries = 2;
    private static readonly TimeSpan RateLimitPause = TimeSpan.FromSeconds(2);

    private readonly IChatOutboxRepository? _repository;
    private readonly IChatMediaUploader? _uploader;
    private readonly IChatImageService? _images;
    private readonly ChatAttachmentOptions? _attachmentOptions;
    private readonly ITaskTracker? _tasks;
    private readonly ILogger _logger;
    private readonly Dictionary<string, CoordinatorOutbox> _coordinators = new();

    // Writes go one after another, in the order they were asked for, so a deletion never overtakes its message
    private Task _writes = Task.CompletedTask;

    /// <summary>A message was sent from a composer, or read back from the database.</summary>
    public event Action<ChatOutgoingMessage>? Added;

    /// <summary>The user gave a message up or took it back into the composer: it leaves its chat.</summary>
    public event Action<ChatOutgoingMessage>? Removed;

    /// <summary>
    /// The server took a message: the stored one, which replaces it in its chat; null from servers that do not send it
    /// back, whose notification of the message came already.
    /// </summary>
    public event Action<ChatOutgoingMessage, ChatMessageModel?>? Sent;

    /// <param name="repository">Null keeps the messages in memory only.</param>
    /// <param name="uploader">Null where messages go without files.</param>
    /// <param name="images">With <paramref name="attachmentOptions"/>, makes the files of the messages read back.</param>
    public ChatOutbox(IChatOutboxRepository? repository = null, IChatMediaUploader? uploader = null,
        IChatImageService? images = null, ChatAttachmentOptions? attachmentOptions = null, ITaskTracker? tasks = null,
        ILogger<ChatOutbox>? logger = null)
    {
        _repository = repository;
        _uploader = uploader;
        _images = images;
        _attachmentOptions = attachmentOptions;
        _tasks = tasks;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    private sealed class CoordinatorOutbox(string id)
    {
        public string Id { get; } = id;
        public ICoordinatorSession? Session { get; set; }
        public Action<CoordinatorState>? OnStateChanged { get; set; }
        public Dictionary<int, List<ChatOutgoingMessage>> Chats { get; } = new();
        // The chats whose queue is being worked through
        public HashSet<int> Running { get; } = new();
        public bool IsLoaded { get; set; }
    }

    /// <summary>
    /// The connection of a coordinator sends its messages from now on; the first one also brings back what was left
    /// from the last run. Called again with the same session, it does nothing.
    /// </summary>
    public void Attach(ICoordinatorSession session)
    {
        var outbox = OutboxOf(session.GetCurrentUser().CoordinatorInstanceId);
        if (ReferenceEquals(outbox.Session, session))
            return;

        if (outbox.Session != null && outbox.OnStateChanged != null)
            outbox.Session.StateChanged -= outbox.OnStateChanged;
        outbox.Session = session;
        // Raised on the transport thread
        outbox.OnStateChanged = state => Dispatcher.UIThread.Post(() => OnStateChanged(outbox, state));
        session.StateChanged += outbox.OnStateChanged;

        if (!outbox.IsLoaded)
        {
            outbox.IsLoaded = true;
            _ = RestoreAsync(outbox);
        }
        else
        {
            ResumeAll(outbox);
        }
    }

    /// <summary>The messages of a chat still on their way, oldest first.</summary>
    public IReadOnlyList<ChatOutgoingMessage> Get(string coordinatorInstanceId, int chatId) =>
        _coordinators.TryGetValue(coordinatorInstanceId, out var outbox) && outbox.Chats.TryGetValue(chatId, out var queue)
            ? queue.ToList()
            : [];

    /// <summary>Takes a message from the composer: it is shown in its chat at once and goes after those before it.</summary>
    public void Enqueue(ICoordinatorSession session, ChatOutgoingMessage message)
    {
        Attach(session);
        var outbox = OutboxOf(message.CoordinatorInstanceId);
        QueueOf(outbox, message.ChatId).Add(message);
        message.State = IsConnected(outbox) ? ChatOutgoingState.Queued : ChatOutgoingState.WaitingForNetwork;
        Save(message);
        Added?.Invoke(message);
        Pump(outbox, message.ChatId);
    }

    /// <summary>Tries a failed message again, in its place among the messages of its chat.</summary>
    public void Retry(ChatOutgoingMessage message)
    {
        if (!message.IsFailed || !message.CanRetry || message.IsRemoved
            || !_coordinators.TryGetValue(message.CoordinatorInstanceId, out var outbox))
            return;

        message.Error = null;
        message.AutomaticRetries = 0;
        foreach (var attachment in message.Attachments)
            attachment.Error = null;
        message.State = IsConnected(outbox) ? ChatOutgoingState.Queued : ChatOutgoingState.WaitingForNetwork;
        Pump(outbox, message.ChatId);
    }

    /// <summary>
    /// Gives a message up: it leaves its chat and its uploads stop. One already on its way may still arrive, and then
    /// shows as any other message.
    /// </summary>
    public void Cancel(ChatOutgoingMessage message)
    {
        if (Remove(message))
            foreach (var attachment in message.Attachments)
                attachment.Dispose();
    }

    /// <summary>
    /// Takes a message back to be put into the composer again: it leaves its chat, and its files are free to be
    /// changed and belong to whoever takes them now.
    /// </summary>
    /// <returns>False when the message is not waiting anymore.</returns>
    public bool TakeBack(ChatOutgoingMessage message)
    {
        if (message.State == ChatOutgoingState.Sending || !Remove(message))
            return false;

        foreach (var attachment in message.Attachments)
        {
            attachment.IsLocked = false;
            attachment.IsUploading = false;
            attachment.Error = null;
        }
        return true;
    }

    private bool Remove(ChatOutgoingMessage message)
    {
        if (message.IsRemoved || !_coordinators.TryGetValue(message.CoordinatorInstanceId, out var outbox)
            || !outbox.Chats.TryGetValue(message.ChatId, out var queue) || !queue.Remove(message))
            return false;

        message.IsRemoved = true;
        message.Attempt?.Cancel();
        Delete(message);
        Removed?.Invoke(message);
        return true;
    }

    private CoordinatorOutbox OutboxOf(string coordinatorInstanceId)
    {
        if (!_coordinators.TryGetValue(coordinatorInstanceId, out var outbox))
            _coordinators[coordinatorInstanceId] = outbox = new CoordinatorOutbox(coordinatorInstanceId);
        return outbox;
    }

    private static List<ChatOutgoingMessage> QueueOf(CoordinatorOutbox outbox, int chatId)
    {
        if (!outbox.Chats.TryGetValue(chatId, out var queue))
            outbox.Chats[chatId] = queue = new List<ChatOutgoingMessage>();
        return queue;
    }

    private static bool IsConnected(CoordinatorOutbox outbox) =>
        outbox.Session?.ConnectionStatus == CoordinatorState.Connected;

    private void OnStateChanged(CoordinatorOutbox outbox, CoordinatorState state)
    {
        if (state == CoordinatorState.Connected)
        {
            ResumeAll(outbox);
            return;
        }

        // What waits for its turn shows that it waits for the connection; what is on its way finds out by itself
        foreach (var message in outbox.Chats.Values.SelectMany(queue => queue))
        {
            if (message.State == ChatOutgoingState.Queued)
                message.State = ChatOutgoingState.WaitingForNetwork;
        }
    }

    private void ResumeAll(CoordinatorOutbox outbox)
    {
        if (!IsConnected(outbox))
            return;

        foreach (var (chatId, queue) in outbox.Chats)
        {
            foreach (var message in queue)
            {
                if (message.State == ChatOutgoingState.WaitingForNetwork)
                    message.State = ChatOutgoingState.Queued;
            }
            Pump(outbox, chatId);
        }
    }

    // One worker per chat goes through its queue; asking again while it runs does nothing, it sees the new messages
    private void Pump(CoordinatorOutbox outbox, int chatId)
    {
        if (outbox.Running.Add(chatId))
            _ = RunAsync(outbox, chatId);
    }

    private async Task RunAsync(CoordinatorOutbox outbox, int chatId)
    {
        try
        {
            while (outbox.Chats.TryGetValue(chatId, out var queue))
            {
                // A failed message waits for the user; those after it still go
                var next = queue.FirstOrDefault(message => message.State is not ChatOutgoingState.Failed);
                if (next == null)
                    return;

                if (outbox.Session is not { ConnectionStatus: CoordinatorState.Connected } session)
                {
                    WaitForNetwork(queue);
                    return;
                }

                if (!await SendAsync(outbox, session, next))
                {
                    WaitForNetwork(queue);
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            LogSendCrashed(exception, chatId);
        }
        finally
        {
            outbox.Running.Remove(chatId);
        }
    }

    private static void WaitForNetwork(List<ChatOutgoingMessage> queue)
    {
        foreach (var message in queue)
        {
            if (message.State is not ChatOutgoingState.Failed)
                message.State = ChatOutgoingState.WaitingForNetwork;
        }
    }

    /// <summary>One attempt to send a message.</summary>
    /// <returns>False when it has to wait for the connection; otherwise it went, failed, is to be tried again or
    /// was removed meanwhile.</returns>
    private async Task<bool> SendAsync(CoordinatorOutbox outbox, ICoordinatorSession session, ChatOutgoingMessage message)
    {
        using var attempt = message.Attempt = new CancellationTokenSource();
        try
        {
            switch (await UploadAsync(session, message, attempt.Token))
            {
                case UploadOutcome.Done:
                    break;
                case UploadOutcome.Disconnected:
                    return false;
                default:
                    return true;
            }

            if (message.IsRemoved)
                return true;

            message.State = ChatOutgoingState.Sending;
            var (result, stored) = await session.SendChatMessage(message.ToNewMessage());

            if (result.IsSuccess)
            {
                if (outbox.Chats.TryGetValue(message.ChatId, out var queue))
                    queue.Remove(message);
                // Given up while it was on its way, it arrived all the same and shows as the stored message
                if (!message.IsRemoved)
                {
                    message.IsRemoved = true;
                    Delete(message);
                    Sent?.Invoke(message, stored);
                }
                foreach (var attachment in message.Attachments)
                    attachment.Dispose();
                return true;
            }

            return await OnRefusedAsync(session, message, result);
        }
        finally
        {
            message.Attempt = null;
        }
    }

    private async Task<bool> OnRefusedAsync(ICoordinatorSession session, ChatOutgoingMessage message, RequestResult result)
    {
        switch (result.Error)
        {
            // Lost on the way: whether the server got it or not, it is stored once
            case ErrorCode.TimedOut or ErrorCode.NotConnected:
                if (session.ConnectionStatus != CoordinatorState.Connected)
                    return false;
                RetryOrFail(message, "chat.send.timedOut");
                return true;
            // The server takes only compressed images, which the files are now
            case ErrorCode.ImageNotCompressed:
                foreach (var attachment in message.Attachments)
                {
                    attachment.ApplyPolicy(session.CoordinatorModel?.ImagePolicy ?? ImageCompressionPolicy.Default);
                    attachment.ResetUpload();
                }
                RetryOrFail(message, "chat.send.imageNotCompressed");
                return true;
            case ErrorCode.VideoNotCompressed:
                foreach (var attachment in message.Attachments)
                {
                    attachment.ApplyVideoPolicy(session.CoordinatorModel?.VideoPolicy ?? VideoCompressionPolicy.Default);
                    attachment.ResetUpload();
                }
                RetryOrFail(message, "chat.send.videoNotCompressed");
                return true;
            // The server does not hold the uploads anymore: the files go again
            case ErrorCode.AttachmentNotReady or ErrorCode.AttachmentInvalid:
                foreach (var attachment in message.Attachments)
                    attachment.ResetUpload();
                RetryOrFail(message, "chat.send.imagesLost");
                return true;
            case ErrorCode.RateLimited:
                await Task.Delay(RateLimitPause);
                RetryOrFail(message, "chat.send.refused");
                return true;
            default:
                Fail(message, Strings.Get(result.Error switch
                {
                    ErrorCode.AttachmentsNotAllowed => "chat.attach.turnedOff",
                    ErrorCode.InsufficientPermissions => "chat.input.noPermission",
                    _ => "chat.send.refused"
                }));
                return true;
        }
    }

    private static void RetryOrFail(ChatOutgoingMessage message, string reasonKey)
    {
        if (message.IsRemoved)
            return;

        if (++message.AutomaticRetries > MaxAutomaticRetries)
        {
            Fail(message, Strings.Get(reasonKey));
            return;
        }

        message.State = ChatOutgoingState.Queued;
    }

    private static void Fail(ChatOutgoingMessage message, string reason)
    {
        if (message.IsRemoved)
            return;

        message.Error = reason;
        message.State = ChatOutgoingState.Failed;
    }

    private enum UploadOutcome { Done, Disconnected, Stopped }

    /// <summary>
    /// Uploads the files one after another, each showing its compression and progress; what was uploaded before is
    /// kept and continued.
    /// </summary>
    private async Task<UploadOutcome> UploadAsync(ICoordinatorSession session, ChatOutgoingMessage message,
        CancellationToken cancellationToken)
    {
        foreach (var attachment in message.Attachments)
        {
            if (attachment.IsImage ? attachment.Upload.Image != null : attachment.FileUpload.File != null)
                continue;

            if (_uploader == null)
            {
                Fail(message, Strings.Get("chat.attach.error.failed"));
                return UploadOutcome.Stopped;
            }

            attachment.Error = null;
            attachment.IsUploading = true;
            message.State = attachment.IsPreparing ? ChatOutgoingState.Preparing : ChatOutgoingState.Uploading;
            // Cancelled from the task list the same way as from the message: it is given up
            using var task = _tasks?.Begin(TrackedTaskKind.Upload,
                attachment.IsPreparing ? TrackedTaskStage.Waiting : TrackedTaskStage.Uploading, attachment.FileName,
                message.TaskContext, () => Cancel(message));
            ChatMediaUploadResult result;
            try
            {
                var progress = new Progress<double>(share =>
                {
                    attachment.Progress = share;
                    task?.Report(share);
                });
                // A compressed copy may still be in the making
                ChatMediaSource? source;
                try { source = attachment.IsImage || attachment.IsVideo ? await attachment.GetUploadSourceAsync(cancellationToken) : null; }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return UploadOutcome.Stopped; }
                if (message.IsRemoved)
                    return UploadOutcome.Stopped;
                message.State = ChatOutgoingState.Uploading;
                task?.SetStage(TrackedTaskStage.Uploading);
                task?.SetTotalBytes((source ?? attachment.Source).Size);

                result = source != null && attachment.IsImage
                    ? await _uploader.UploadImageAsync(session, message.ChatId, source, attachment.Upload,
                        progress, cancellationToken)
                    : ChatMediaUploadResult.NotAnImage;

                // Named like a picture but not one the chat can show: it is still a file to send
                if (result == ChatMediaUploadResult.NotAnImage)
                {
                    // A video goes as the file it was compressed to
                    if (!attachment.IsVideo)
                        attachment.SendAsFile();
                    result = await _uploader.UploadFileAsync(session, message.ChatId,
                        attachment.IsVideo ? source ?? attachment.Source : attachment.Source,
                        attachment.FileUpload, progress, cancellationToken);
                }
            }
            finally
            {
                attachment.IsUploading = false;
            }

            if (result == ChatMediaUploadResult.NoError)
            {
                attachment.Progress = 1;
                task?.Complete();
                continue;
            }

            if (result == ChatMediaUploadResult.Canceled || message.IsRemoved)
                return UploadOutcome.Stopped;

            if (result == ChatMediaUploadResult.ConnectionFailed)
                return UploadOutcome.Disconnected;

            attachment.Error = Strings.Get(result switch
            {
                ChatMediaUploadResult.NotAnImage => "chat.attach.error.notAnImage",
                ChatMediaUploadResult.Empty => "chat.attach.error.empty",
                ChatMediaUploadResult.TooLarge => "chat.attach.error.tooLarge",
                ChatMediaUploadResult.NotAllowed => "chat.attach.turnedOff",
                ChatMediaUploadResult.TooManyUploads => "chat.attach.error.tooMany",
                ChatMediaUploadResult.StorageFull => "chat.attach.error.storageFull",
                ChatMediaUploadResult.Unreadable => "chat.attach.error.unreadable",
                _ => "chat.attach.error.failed"
            });
            task?.Fail(attachment.Error);
            Fail(message, Strings.Format("chat.send.imageFailed", ("file", attachment.FileName), ("reason", attachment.Error)));
            return UploadOutcome.Stopped;
        }

        return UploadOutcome.Done;
    }

    /// <summary>Brings back the messages left from the last run and sends them once the connection is up.</summary>
    private async Task RestoreAsync(CoordinatorOutbox outbox)
    {
        if (_repository == null || outbox.Session is not { } session)
            return;

        IReadOnlyList<StoredOutgoingMessage> stored;
        try
        {
            stored = await _repository.GetAllAsync(outbox.Id);
        }
        catch (Exception exception)
        {
            LogLoadFailed(exception);
            return;
        }

        var senderUserId = session.GetCurrentUser().Id;
        foreach (var saved in stored)
        {
            if (outbox.Chats.Values.Any(queue => queue.Any(message => message.ClientMessageId == saved.ClientMessageId)))
                continue;

            var message = Restore(outbox.Id, senderUserId, saved, session);
            QueueOf(outbox, message.ChatId).Add(message);
            Added?.Invoke(message);
        }

        // Read later than messages sent meanwhile, they still go first
        foreach (var queue in outbox.Chats.Values)
            queue.Sort((left, right) => left.CreatedAt.CompareTo(right.CreatedAt));
        OnStateChanged(outbox, session.ConnectionStatus);
    }

    private ChatOutgoingMessage Restore(string coordinatorInstanceId, int senderUserId, StoredOutgoingMessage saved,
        ICoordinatorSession session)
    {
        var attachments = new List<ChatAttachmentVM>();
        string? missing = null;
        foreach (var file in saved.Files)
        {
            if (file.Path is not { } path || !File.Exists(path) || _images == null)
            {
                missing ??= file.Name;
                continue;
            }

            var picked = new PickedFile(file.Name, path, file.Size,
                () => Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                    useAsync: true)));
            var preferences = new ChatAttachmentPreferences(file.IsCompressed, file.IsMetadataStripped, file.IsCompressed,
                file.VideoSide);
            attachments.Add(new ChatAttachmentVM(picked, file.IsImage, _images, _attachmentOptions?.Preparer, preferences,
                session.CoordinatorModel?.ImagePolicy, _attachmentOptions?.VideoPreparer, session.CoordinatorModel?.VideoPolicy,
                _tasks));
        }

        var reply = saved.ReplyToMessageId is { } replyTo
            ? new ChatReplyContent
            {
                MessageId = replyTo,
                ChatId = saved.ChatId,
                SenderUserId = saved.ReplySenderUserId,
                TextContent = saved.ReplyText == null ? null : new ChatTextContent { Text = saved.ReplyText },
                HasAttachments = saved.ReplyHasAttachments
            }
            : null;
        var message = new ChatOutgoingMessage(saved.ClientMessageId, coordinatorInstanceId, saved.ChatId, senderUserId,
            saved.CreatedAt, saved.Text == null ? null : new ChatTextContent { Text = saved.Text }, reply, null,
            saved.IsLinkPreviewDisabled, attachments, null, canRetry: missing == null) { IsRestored = true };

        // Without one of its files it would go as another message than the one sent
        if (missing != null)
        {
            message.Error = Strings.Format("chat.send.fileUnavailable", ("file", missing));
            message.State = ChatOutgoingState.Failed;
        }
        else
        {
            message.State = ChatOutgoingState.WaitingForNetwork;
        }

        return message;
    }

    private void Save(ChatOutgoingMessage message)
    {
        if (_repository is not { } repository)
            return;

        var stored = message.ToStored();
        Write(() => repository.SaveAsync(stored), message.ChatId);
    }

    private void Delete(ChatOutgoingMessage message)
    {
        if (_repository is not { } repository)
            return;

        var id = message.ClientMessageId;
        Write(() => repository.DeleteAsync(id), message.ChatId);
    }

    private void Write(Func<Task> write, int chatId)
    {
        _writes = _writes.ContinueWith(async _ =>
        {
            try
            {
                await write();
            }
            catch (Exception exception)
            {
                LogSaveFailed(exception, chatId);
            }
        }, TaskScheduler.Default).Unwrap();
    }

    [LoggerMessage(LogLevel.Error, "Failed to load the messages waiting to be sent")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to save a message waiting to be sent to chat {ChatId}")]
    private partial void LogSaveFailed(Exception exception, int chatId);

    [LoggerMessage(LogLevel.Error, "Sending the messages of chat {ChatId} stopped")]
    private partial void LogSendCrashed(Exception exception, int chatId);
}
