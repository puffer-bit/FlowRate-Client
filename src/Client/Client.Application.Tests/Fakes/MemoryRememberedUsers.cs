using System.Collections.Concurrent;
using Client.Application.Ports.Repositories;
using Client.Core.Users;

namespace Client.Application.Tests.Fakes;

/// <summary>
/// Remembers every user it is asked about. A load can be held until <see cref="LoadGate"/> completes.
/// </summary>
internal sealed class MemoryRememberedUsers : IRememberedUsersRepository
{
    public ConcurrentDictionary<int, UserHybridSessionData> Data { get; } = new();

    public TaskCompletionSource? LoadGate { get; set; }

    public Task SaveUserAsync(UserModel user) => Task.CompletedTask;

    public Task<UserHybridSessionData> GetHybridSessionDataAsync(string coordinatorInstanceId, int userId) =>
        Task.FromResult(Data.GetValueOrDefault(userId) ?? new UserHybridSessionData());

    public async Task<IReadOnlyDictionary<int, UserHybridSessionData>> GetHybridSessionDataAsync(
        string coordinatorInstanceId, IReadOnlyCollection<int> userIds)
    {
        if (LoadGate != null)
            await LoadGate.Task;

        return userIds.ToDictionary(id => id, id => Data.GetValueOrDefault(id) ?? new UserHybridSessionData());
    }

    public Task<bool> SaveHybridSessionDataAsync(string coordinatorInstanceId, int userId, UserHybridSessionData data)
    {
        Data[userId] = data;
        return Task.FromResult(true);
    }
}
