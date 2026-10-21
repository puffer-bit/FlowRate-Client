namespace Client.Core.Chat;

/// <summary>
/// A run of consecutive messages of a chat, oldest first.
/// </summary>
/// <param name="Revision">The revision of the chat the messages are at least as fresh as.</param>
public sealed record ChatHistoryPage(IReadOnlyList<ChatMessageModel> Messages, long Revision);
