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

    /// <returns>Null when nothing is remembered.</returns>
    Task<LastSession?> GetLastSessionAsync(string coordinatorInstanceId);

    /// <param name="session">Null forgets it.</param>
    Task SetLastSessionAsync(string coordinatorInstanceId, LastSession? session);
}
