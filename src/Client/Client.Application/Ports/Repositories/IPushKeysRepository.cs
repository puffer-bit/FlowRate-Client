using Client.Core.Push;

namespace Client.Application.Ports.Repositories;

/// <summary>
/// The keys coordinators encrypt their push notifications with, one per coordinator.
/// </summary>
public interface IPushKeysRepository
{
    Task<PushKey?> GetByCoordinatorAsync(string coordinatorInstanceId);
    Task<PushKey?> GetByKeyIdAsync(string keyId);

    /// <summary>
    /// Stores the key, replacing the one its coordinator had.
    /// </summary>
    Task SaveAsync(PushKey key);

    /// <summary>
    /// Remembers where replies to the notifications of the coordinator go. Kept until the key is removed.
    /// </summary>
    Task SetReplyTargetAsync(string coordinatorInstanceId, PushReplyTarget target);

    /// <returns>Null when the coordinator gave no reply token, or before it did.</returns>
    Task<PushReplyTarget?> GetReplyTargetAsync(string coordinatorInstanceId);
    Task RemoveAsync(string coordinatorInstanceId);
}
