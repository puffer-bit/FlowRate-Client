using Client.Application.Ports.Repositories;
using Client.Core.Chat;

namespace Client.Application.Chat;

/// <summary>
/// The cached tail of one chat, see <see cref="IChatCacheRepository"/>. Calls are carried out one
/// by one in the order they were made. The cache is an optimization, so a failure of the storage
/// is logged and reads answer as if nothing was cached.
/// </summary>
public interface IChatCache
{
    /// <inheritdoc cref="IChatCacheRepository.GetNewestAsync"/>
    Task<CachedChatPage?> LoadNewestAsync(int take);

    /// <inheritdoc cref="IChatCacheRepository.GetBeforeAsync"/>
    Task<CachedChatPage?> LoadBeforeAsync(int beforeMessageId, int take);

    /// <inheritdoc cref="IChatCacheRepository.ReplaceAsync"/>
    void Replace(IReadOnlyList<ChatMessageModel> messages, long revision, bool reachesStart);

    /// <inheritdoc cref="IChatCacheRepository.AddOlderAsync"/>
    void AddOlder(int beforeMessageId, IReadOnlyList<ChatMessageModel> messages, bool reachesStart);

    /// <summary>
    /// Applies changed messages in their current state, a deleted one being marked with
    /// <see cref="ChatMessageModel.IsDeleted"/>, and moves the tail to <paramref name="revision"/>.
    /// </summary>
    void Apply(IReadOnlyList<ChatMessageModel> changes, long revision);

    void Remove(int messageId, long revision);
}

public interface IChatCacheStore
{
    /// <summary>
    /// False when the user does not want messages kept: nothing is read from the caches and
    /// nothing written to them, also by the caches opened before.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <param name="currentUserId">Whose messages come out of the cache marked as mine.</param>
    IChatCache Open(string coordinatorInstanceId, int chatId, int currentUserId);

    /// <summary>
    /// Forgets a chat that was deleted.
    /// </summary>
    void Remove(string coordinatorInstanceId, int chatId);

    /// <summary>
    /// Forgets the coordinator's chats that are not among <paramref name="existingChatIds"/>:
    /// a deletion that happened while the client was away was never announced.
    /// </summary>
    void RemoveExcept(string coordinatorInstanceId, IReadOnlyCollection<int> existingChatIds);
}
