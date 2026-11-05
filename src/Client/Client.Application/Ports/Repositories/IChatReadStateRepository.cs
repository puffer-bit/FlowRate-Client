using Client.Core.Chat;

namespace Client.Application.Ports.Repositories;

public interface IChatReadStateRepository
{
    Task<IReadOnlyList<ChatReadState>> GetAsync(string coordinatorInstanceId, int userId);
    Task SetAsync(string coordinatorInstanceId, int userId, ChatReadState state);
}
