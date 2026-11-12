namespace Client.Core.Chat;

public record ChatHistoryWindow(List<ChatMessageModel> Messages, long Revision, bool HasOlder, bool HasNewer);
