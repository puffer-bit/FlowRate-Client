using Client.Core.Chat;

namespace Client.Application.Ports.Repositories;

/// <summary>
/// The messages of chats kept between runs. Of each chat one run of its newest messages without
/// gaps is kept, the tail, together with the revision of the chat the tail is current at.
/// <see cref="ChatMessageModel.IsMine"/> is not kept: it depends on who is logged in.
/// </summary>
public interface IChatCacheRepository
{
    /// <summary>
    /// The tail of a chat is cut down to this many messages; older ones come from the server again.
    /// A lower number takes effect for a chat the next time its tail changes.
    /// </summary>
    int MaxMessages { get; set; }

    /// <summary>How many messages are kept in all, and the size of what is kept of them.</summary>
    Task<(int Messages, long Bytes)> GetUsageAsync();

    /// <summary>Removes the tails of every chat of every coordinator.</summary>
    Task RemoveAllAsync();

    /// <summary>
    /// The newest messages of the tail, oldest first. Null when the chat has no tail.
    /// </summary>
    Task<CachedChatPage?> GetNewestAsync(ChatCacheKey chat, int take);

    /// <summary>
    /// The messages of the tail that precede <paramref name="beforeMessageId"/>, oldest first.
    /// Null when the chat has no tail.
    /// </summary>
    Task<CachedChatPage?> GetBeforeAsync(ChatCacheKey chat, int beforeMessageId, int take);

    /// <summary>
    /// Replaces the tail with the newest page of the chat.
    /// </summary>
    /// <param name="reachesStart">The page begins with the first message of the chat.</param>
    Task ReplaceAsync(ChatCacheKey chat, IReadOnlyList<ChatMessageModel> messages, long revision, bool reachesStart);

    /// <summary>
    /// Extends the tail with the page that precedes <paramref name="beforeMessageId"/>. Does nothing
    /// when that message is not the oldest of the tail, since the page would leave a gap.
    /// </summary>
    Task AddOlderAsync(ChatCacheKey chat, int beforeMessageId, IReadOnlyList<ChatMessageModel> messages,
        bool reachesStart);

    /// <summary>
    /// Applies the current state of changed messages by their ids and moves the tail to
    /// <paramref name="revision"/> unless it is ahead already. A new message is added only when it
    /// continues the tail; quotes of a changed message follow it wherever that message is.
    /// Does nothing when the chat has no tail.
    /// </summary>
    Task ApplyAsync(ChatCacheKey chat, IReadOnlyList<ChatMessageModel> changed, IReadOnlyList<int> deletedIds,
        long revision);

    Task RemoveAsync(ChatCacheKey chat);

    /// <summary>
    /// Removes the tails of the coordinator's chats that are not among <paramref name="existingChatIds"/>.
    /// </summary>
    Task RemoveExceptAsync(string coordinatorInstanceId, IReadOnlyCollection<int> existingChatIds);
}

/// <summary>
/// Chat ids are only unique within a coordinator.
/// </summary>
public readonly record struct ChatCacheKey(string CoordinatorInstanceId, int ChatId);

/// <param name="Revision">The revision of the chat the tail is current at.</param>
/// <param name="ReachesStart">Nothing precedes these messages, neither in the tail nor on the server.</param>
public sealed record CachedChatPage(IReadOnlyList<ChatMessageModel> Messages, long Revision, bool ReachesStart);
