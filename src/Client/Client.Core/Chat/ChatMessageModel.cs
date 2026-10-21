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

    public ChatTextContent? TextContent { get; init; }
    public ChatReplyContent? ReplyContent { get; init; }

    public required List<ChatImageContent> Images { get; init; }
    public required List<ChatVideoContent> Video { get; init; }
    public required List<ChatFileContent> Files { get; init; }

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
    public bool IsDisposable { get; init; }
    public bool IsMine { get; set; }

    /// <summary>
    /// Whether the message calls the given user: by name, or everyone in the chat. A mention of
    /// those online counts too, since whoever sees the message as it comes is online.
    /// </summary>
    public bool Mentions(int userId) =>
        MentionsEveryone || MentionsHere || MentionedUserIds.Contains(userId);
}
