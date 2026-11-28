using Client.Core.Settings;


namespace Client.Application.Ports.Repositories;

public interface IAccountingSettingsRepository
{
    Task<IReadOnlyList<SavedCoordinatorModel>> GetAllCoordinatorsAsync();
    Task<SavedCoordinatorModel?> GetCoordinatorAsync(string coordinatorId);
    Task<(SavedCoordinatorModel Model, SavedUserModel User)?> GetCoordinatorInstanceAsync(
        (string CoordinatorId, int UserId) identityIdsPair);
    
    Task SaveUserAsync(SavedUserModel model);

    /// <summary>Changes the name of a saved identity and nothing else. Does nothing for an identity not saved.</summary>
    Task RenameUserAsync(string coordinatorId, int userId, string username);

    /// <summary>
    /// Keeps the device a saved account logs in as, or forgets it when null, for <paramref name="lostReason"/>. Does
    /// nothing for an account not saved.
    /// </summary>
    Task SetDeviceAsync(string coordinatorId, int userId, Guid? deviceId, string? deviceKey,
        LoginLostReason? lostReason = null);

    /// <summary>
    /// Records a successful connection of the saved account: the last one overall and the last one of its server.
    /// </summary>
    Task SetLastConnectionAsync(string coordinatorId, int userId);

    /// <summary>Marks the recovery phrase of a saved account as saved by the user, or not.</summary>
    Task SetRecoveryPhraseSavedAsync(string coordinatorId, int userId, bool isSaved);

    /// <summary>Drops the server password the earlier client kept in the database, once it is in the secret vault.</summary>
    Task ClearServerPasswordAsync(string coordinatorId, int userId);
    Task SaveCoordinatorAsync(SavedCoordinatorModel model);
    Task DeleteCoordinatorAsync(string coordinatorId);
    Task DeleteUserAsync(string coordinatorId, int userId);
    
    Task<DatabaseSettings> GetDatabaseSettingsAsync();
    Task UpdateDatabaseSettingsAsync(DatabaseSettings databaseSettings);
}
