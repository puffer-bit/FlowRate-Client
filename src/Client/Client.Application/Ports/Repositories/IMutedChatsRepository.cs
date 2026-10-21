namespace Client.Application.Ports.Repositories;

/// <summary>
/// The chats whose notifications the user muted, by their ids within a coordinator.
/// </summary>
public interface IMutedChatsRepository
{
    Task<IReadOnlyList<int>> GetAsync(string coordinatorInstanceId);
    Task SetAsync(string coordinatorInstanceId, int chatId, bool isMuted);
}
