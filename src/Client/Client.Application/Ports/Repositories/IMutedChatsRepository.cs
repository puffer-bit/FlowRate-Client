namespace Client.Application.Ports.Repositories;

/// <summary>
/// The chats whose notifications the user muted, by their ids within a coordinator. A mute may end at a time;
/// past it the chat notifies again.
/// </summary>
public interface IMutedChatsRepository
{
    /// <summary>The chats muted now.</summary>
    Task<IReadOnlyList<int>> GetAsync(string coordinatorInstanceId);

    /// <summary>The chats muted now, with the time each mute ends at, null for a mute until the user ends it.</summary>
    Task<IReadOnlyDictionary<int, DateTimeOffset?>> GetUntilAsync(string coordinatorInstanceId);

    /// <param name="until">When the notifications come back by themselves; null keeps them muted.</param>
    Task SetAsync(string coordinatorInstanceId, int chatId, bool isMuted, DateTimeOffset? until = null);
}
