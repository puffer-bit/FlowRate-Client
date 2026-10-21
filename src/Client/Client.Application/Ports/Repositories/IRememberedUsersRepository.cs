using Client.Core.Users;


namespace Client.Application.Ports.Repositories;

/// <summary>
/// Users met on coordinators, together with the local per-user settings for hybrid sessions.
/// User ids are only unique within a coordinator, so every user is addressed by the pair of the
/// coordinator instance id and the user id.
/// </summary>
public interface IRememberedUsersRepository
{
    /// <summary>
    /// Adds the user or refreshes the profile of an already remembered one. The hybrid session
    /// data of a remembered user is kept as it is.
    /// </summary>
    Task SaveUserAsync(UserModel user);

    /// <summary>
    /// Returns the defaults for a user that is not remembered yet.
    /// </summary>
    Task<UserHybridSessionData> GetHybridSessionDataAsync(string coordinatorInstanceId, int userId);

    /// <summary>
    /// Reads the data of several users in one query, for example of every peer of a session being
    /// joined. Users that are not remembered yet get the defaults.
    /// </summary>
    Task<IReadOnlyDictionary<int, UserHybridSessionData>> GetHybridSessionDataAsync(string coordinatorInstanceId, IReadOnlyCollection<int> userIds);

    /// <summary>
    /// Saves the data of a remembered user. Returns false when the user is not remembered, since
    /// the data cannot be stored without the profile: save it with <see cref="SaveUserAsync"/> first.
    /// </summary>
    Task<bool> SaveHybridSessionDataAsync(string coordinatorInstanceId, int userId, UserHybridSessionData data);
}
