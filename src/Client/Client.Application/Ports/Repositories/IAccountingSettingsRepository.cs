using Client.Core.Settings;


namespace Client.Application.Ports.Repositories;

public interface IAccountingSettingsRepository
{
    Task<IReadOnlyList<SavedCoordinatorModel>> GetAllCoordinatorsAsync();
    Task<SavedCoordinatorModel?> GetCoordinatorAsync(string address, int port);
    Task<SavedCoordinatorModel?> GetCoordinatorAsync(string coordinatorId);
    Task<(SavedCoordinatorModel Model, SavedUserModel User)?> GetCoordinatorInstanceAsync(
        (string CoordinatorId, int UserId) identityIdsPair);
    
    Task SaveUserAsync(SavedUserModel model);

    /// <summary>Changes the name of a saved identity and nothing else. Does nothing for an identity not saved.</summary>
    Task RenameUserAsync(string coordinatorId, int userId, string username);

    /// <summary>Keeps or forgets the account password of a saved identity. Does nothing for an identity not saved.</summary>
    Task SetAccountPasswordAsync(string coordinatorId, int userId, string? accountPassword);
    Task SaveCoordinatorAsync(SavedCoordinatorModel model);
    Task DeleteCoordinatorAsync(string coordinatorId);
    
    Task<DatabaseSettings> GetDatabaseSettingsAsync();
    Task UpdateDatabaseSettingsAsync(DatabaseSettings databaseSettings);
}
