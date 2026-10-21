namespace Client.Core.Chat;

public class ChatReplyContent
{
    public required int MessageId { get; init; }
    public int ChatId { get; init; }
    public int? SenderUserId { get; init; }
    public DateTime SentTime { get; init; }
    public ChatTextContent? TextContent { get; init; }
    public bool IsDeleted { get; init; }
}