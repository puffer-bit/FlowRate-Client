namespace Client.Core.Chat;

/// <summary>
/// The messages of a chat changed after some revision, in their current state and ordered by revision.
/// A deleted message comes without content, with <see cref="ChatMessageModel.IsDeleted"/> set.
/// </summary>
/// <param name="Revision">The revision of the chat when the changes were collected.</param>
/// <param name="HasMore">More changes follow; the next request continues from the highest revision received.</param>
/// <param name="Reset">Too much changed to list: there are no messages and the history is to be loaded anew.</param>
public sealed record ChatChanges(IReadOnlyList<ChatMessageModel> Messages, long Revision, bool HasMore, bool Reset);
