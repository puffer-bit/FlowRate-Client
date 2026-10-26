using Client.Core.Coordinator;

namespace Client.Application.Ports.Repositories;

/// <summary>
/// What is remembered about the stay on a coordinator, by its instance id.
/// </summary>
public interface ICoordinatorStateRepository
{
    Task<bool> IsMicrophoneMutedAsync(string coordinatorInstanceId);
    Task SetMicrophoneMutedAsync(string coordinatorInstanceId, bool isMuted);

    /// <summary>
    /// Whether the user silenced the voices of hybrid sessions on the coordinator.
    /// </summary>
    Task<bool> IsSoundMutedAsync(string coordinatorInstanceId);
    Task SetSoundMutedAsync(string coordinatorInstanceId, bool isMuted);

    /// <summary>
    /// Whether the user is away from the keyboard in hybrid sessions on the coordinator, and the reason they gave.
    /// </summary>
    Task<(bool IsAfk, string? Reason)> GetAfkAsync(string coordinatorInstanceId);

    /// <param name="reason">Kept only while away.</param>
    Task SetAfkAsync(string coordinatorInstanceId, bool isAfk, string? reason);

    /// <returns>Null when nothing is remembered.</returns>
    Task<LastSession?> GetLastSessionAsync(string coordinatorInstanceId);

    /// <param name="session">Null forgets it.</param>
    Task SetLastSessionAsync(string coordinatorInstanceId, LastSession? session);
}
