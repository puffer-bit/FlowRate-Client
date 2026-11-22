namespace Client.Core.Chat;

/// <summary>
/// A message the user sends to a chat.
/// </summary>
public class NewChatMessage
{
    public required int ChatId { get; init; }
    public required int SenderUserId { get; init; }
    public required DateTimeOffset SentTime { get; init; }
    public DateTimeOffset? DelayedSentTime { get; init; }
    public DateTimeOffset? DisposeTime { get; init; }

    public ChatTextContent? TextContent { get; init; }
    public ChatReplyContent? ReplyContent { get; init; }

    public List<NewChatImage> Images { get; init; } = new();
    public List<NewChatFile> Files { get; init; } = new();

    public bool IsDelayed { get; init; }
    public bool IsDisposable { get; init; }

    /// <summary>The author closed the preview of the link in the composer.</summary>
    public bool IsLinkPreviewDisabled { get; init; }
}
