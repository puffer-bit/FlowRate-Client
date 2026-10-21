namespace Client.Core.Chat;

/// <summary>
/// A message the user sends to a chat.
/// </summary>
public class NewChatMessage
{
    public required int ChatId { get; init; }
    public required int SenderUserId { get; init; }
    public required DateTime SentTime { get; init; }
    public DateTime? DelayedSentTime { get; init; }
    public DateTime? DisposeTime { get; init; }

    public ChatTextContent? TextContent { get; init; }
    public ChatReplyContent? ReplyContent { get; init; }

    public List<NewChatImage> Images { get; init; } = new();
    public List<ChatVideoContent> Video { get; init; } = new();
    public List<ChatFileContent> Files { get; init; } = new();

    public bool IsDelayed { get; init; }
    public bool IsDisposable { get; init; }
}
