using Shared.Models.DataTransferObjects.ChatSession.Messages;

namespace Client.Core.Chat;

public class ChatMessageModel
{
    public required int Id { get; init; }
    public required int ChatId { get; init; }

    /// <summary>
    /// The revision of the chat at which this message was last changed.
    /// </summary>
    public long Revision { get; init; }

    public required int SenderUserId { get; init; }
    public required DateTime SentTime { get; init; }
    public DateTime? DelayedSentTime { get; init; }
    public DateTime? DisposeTime { get; init; }
    public DateTime? EditTime { get; init; }
    public DateTime? PinnedAt { get; init; }
    public int? PinnedByUserId { get; init; }
    public bool IsPinned => PinnedAt.HasValue;

    public ChatTextContent? TextContent { get; init; }
    public ChatReplyContent? ReplyContent { get; init; }

    public required List<ChatImageContent> Images { get; init; }
    public required List<ChatVideoContent> Video { get; init; }
    public required List<ChatFileContent> Files { get; init; }

    /// <summary>The preview of the first link of the text, once the server has fetched its page.</summary>
    public ChatLinkPreview? LinkPreview { get; init; }

    /// <summary>The author sent the message without a preview of its link.</summary>
    public bool IsLinkPreviewDisabled { get; init; }

    public required List<ChatReaction> Reactions { get; init; }
    public required List<string> UsersIds { get; init; }

    /// <summary>
    /// Who the text mentions, read by the server from its mention tokens.
    /// </summary>
    public List<int> MentionedUserIds { get; init; } = [];
    public bool MentionsEveryone { get; init; }
    public bool MentionsHere { get; init; }

    public bool IsDelayed { get; init; }
    public bool IsEdited { get; init; }
    public bool IsReposted { get; init; }
    public bool IsDelivered { get; init; }
    public bool IsDeleted { get; init; }
    public bool IsReadOnly { get; init; }
    public bool IsSystem { get; init; }
    public ChatSystemContent? SystemContent { get; init; }
    public bool IsDisposable { get; init; }
    public bool IsMine { get; set; }

    /// <summary>
    /// Whether the message calls the given user: by name, or everyone in the chat. A mention of
    /// those online counts too, since whoever sees the message as it comes is online.
    /// </summary>
    public bool Mentions(int userId) =>
        MentionsEveryone || MentionsHere || MentionedUserIds.Contains(userId);
}
