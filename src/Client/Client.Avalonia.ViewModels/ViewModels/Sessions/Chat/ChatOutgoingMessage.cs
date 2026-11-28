using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Client.Application.Ports.Repositories;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using ReactiveUI;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>Where a message sent from this client is on its way to the server.</summary>
public enum ChatOutgoingState
{
    /// <summary>Waits for the messages sent before it in the chat.</summary>
    Queued,
    /// <summary>A file of it is being compressed.</summary>
    Preparing,
    /// <summary>A file of it is being uploaded.</summary>
    Uploading,
    /// <summary>Its files are on the server and the message itself is on its way.</summary>
    Sending,
    /// <summary>The connection is down; it goes once the server can be reached again.</summary>
    WaitingForNetwork,
    /// <summary>The server refused it, or it could not go: it waits for the user to try again or give it up.</summary>
    Failed
}

/// <summary>
/// A message sent from this client that the server has not taken yet. It is shown in its chat at once, under the
/// id <see cref="LocalId"/>, and replaced by the stored message once that comes back with the same
/// <see cref="ClientMessageId"/>. Owned by <see cref="ChatOutbox"/>, which sends it, and changed on the UI thread only.
/// </summary>
public sealed class ChatOutgoingMessage : ReactiveObject
{
    private static int _lastLocalId;

    /// <summary>Kept for every attempt, so the server stores the message once however often it goes.</summary>
    public Guid ClientMessageId { get; }
    public string CoordinatorInstanceId { get; }
    public int ChatId { get; }
    public int SenderUserId { get; }

    /// <summary>When the user sent it; the chat shows it at this time until the server gives its own.</summary>
    public DateTimeOffset CreatedAt { get; }

    public ChatTextContent? TextContent { get; }

    /// <summary>The message it answers, as its quote shows it.</summary>
    public ChatReplyContent? Reply { get; }

    /// <summary>The preview the composer showed for the link of the text, shown again until the server sends its own.</summary>
    public ChatLinkPreview? LinkPreview { get; }

    public bool IsLinkPreviewDisabled { get; }

    /// <summary>The files, locked as they were when it was sent; each shows its compression and upload.</summary>
    public IReadOnlyList<ChatAttachmentVM> Attachments { get; }

    public bool HasAttachments => Attachments.Count > 0;

    /// <summary>The chat the task list names for its uploads.</summary>
    public string? TaskContext { get; }

    /// <summary>
    /// The id the message has in its chat until the server gives it one: below zero, so never one of the server.
    /// </summary>
    public int LocalId { get; } = Interlocked.Decrement(ref _lastLocalId);

    /// <summary>A file it had before the client restarted cannot be read again: it cannot go as it was.</summary>
    public bool CanRetry { get; }

    /// <summary>Read back from the database after a restart rather than sent in this run.</summary>
    public bool IsRestored { get; init; }

    public ChatOutgoingState State
    {
        get;
        internal set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsFailed));
            this.RaisePropertyChanged(nameof(IsWaitingForNetwork));
            this.RaisePropertyChanged(nameof(IsPreparing));
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    public bool IsFailed => State == ChatOutgoingState.Failed;
    public bool IsWaitingForNetwork => State == ChatOutgoingState.WaitingForNetwork;
    public bool IsPreparing => State == ChatOutgoingState.Preparing;

    /// <summary>Why it did not go; null unless <see cref="IsFailed"/>.</summary>
    public string? Error
    {
        get;
        internal set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>What the status mark of the message says on hover.</summary>
    public string StatusText => State switch
    {
        ChatOutgoingState.Failed => Error ?? Strings.Get("chat.outgoing.failed"),
        ChatOutgoingState.WaitingForNetwork => Strings.Get("chat.outgoing.waitingForNetwork"),
        ChatOutgoingState.Preparing => Strings.Get("chat.outgoing.compressing"),
        ChatOutgoingState.Queued => Strings.Get("chat.outgoing.queued"),
        _ => Strings.Get("chat.outgoing.sending")
    };

    // Set by the outbox: the attempts made after refusals that a new attempt may cure, the attempt going on,
    // and whether the user gave the message up or took it back
    internal int AutomaticRetries { get; set; }
    internal CancellationTokenSource? Attempt { get; set; }
    internal bool IsRemoved { get; set; }

    public ChatOutgoingMessage(string coordinatorInstanceId, int chatId, int senderUserId, ChatTextContent? textContent,
        ChatReplyContent? reply, ChatLinkPreview? linkPreview, bool isLinkPreviewDisabled,
        IReadOnlyList<ChatAttachmentVM> attachments, string? taskContext = null)
        : this(Guid.NewGuid(), coordinatorInstanceId, chatId, senderUserId, DateTimeOffset.UtcNow, textContent, reply,
            linkPreview, isLinkPreviewDisabled, attachments, taskContext, canRetry: true)
    {
    }

    internal ChatOutgoingMessage(Guid clientMessageId, string coordinatorInstanceId, int chatId, int senderUserId,
        DateTimeOffset createdAt, ChatTextContent? textContent, ChatReplyContent? reply, ChatLinkPreview? linkPreview,
        bool isLinkPreviewDisabled, IReadOnlyList<ChatAttachmentVM> attachments, string? taskContext, bool canRetry)
    {
        ClientMessageId = clientMessageId;
        CoordinatorInstanceId = coordinatorInstanceId;
        ChatId = chatId;
        SenderUserId = senderUserId;
        CreatedAt = createdAt;
        TextContent = textContent;
        Reply = reply;
        LinkPreview = linkPreview;
        IsLinkPreviewDisabled = isLinkPreviewDisabled;
        Attachments = attachments;
        TaskContext = taskContext;
        CanRetry = canRetry;
        foreach (var attachment in attachments)
            attachment.IsLocked = true;
    }

    /// <summary>The message as the chat shows it until the stored one replaces it.</summary>
    public ChatMessageModel ToModel() => new()
    {
        Id = LocalId,
        ChatId = ChatId,
        SenderUserId = SenderUserId,
        ClientMessageId = ClientMessageId,
        SentTime = CreatedAt,
        TextContent = TextContent,
        ReplyContent = Reply,
        // The files are shown from the attachments, with their uploads, until the server has them
        Images = [],
        Files = [],
        LinkPreview = IsLinkPreviewDisabled ? null : LinkPreview,
        IsLinkPreviewDisabled = IsLinkPreviewDisabled,
        Reactions = [],
        IsMine = true
    };

    /// <summary>What goes to the server, once every file is uploaded.</summary>
    internal NewChatMessage ToNewMessage() => new()
    {
        ChatId = ChatId,
        ClientMessageId = ClientMessageId,
        SenderUserId = SenderUserId,
        SentTime = CreatedAt,
        TextContent = TextContent,
        ReplyContent = Reply == null ? null : new ChatReplyContent { MessageId = Reply.MessageId },
        Images = Attachments.Where(attachment => attachment.IsImage)
            .Select(attachment => attachment.Upload.Image!).ToList(),
        Files = Attachments.Where(attachment => attachment.IsFile)
            .Select(attachment => attachment.FileUpload.File!).ToList(),
        IsLinkPreviewDisabled = IsLinkPreviewDisabled
    };

    /// <summary>What is kept of it, so it still goes after a restart.</summary>
    internal StoredOutgoingMessage ToStored() => new(
        ClientMessageId, CoordinatorInstanceId, ChatId, CreatedAt, TextContent?.Text, IsLinkPreviewDisabled,
        Reply?.MessageId, Reply?.SenderUserId, Reply?.TextContent?.Text, Reply?.HasAttachments == true,
        Attachments.Select(attachment => new StoredOutgoingFile(attachment.FileName, attachment.Source.LocalPath,
            attachment.Size, attachment.IsImage, attachment.IsCompressed, attachment.IsMetadataStripped,
            attachment.IsVideo ? attachment.VideoSide : null)).ToList());
}
