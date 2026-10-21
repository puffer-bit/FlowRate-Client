using Client.Core.Settings;


namespace Client.Application.Settings;

public interface IBehaviorService
{
    Task<(SavedCoordinatorModel Coordinator, SavedUserModel User)?> GetAutoConnectInstance();
    Task SetLastIdentityAsync((string CoordinatorId, int UserId) identity);
    Task RemoveLastIdentityAsync();
    
    Task<BehaviorSettings> GetSettingsAsync();
    Task SaveSettingsAsync(BehaviorSettings settings);
}