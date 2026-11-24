using Client.Core.Settings;


namespace Client.Application.Settings;

public interface IBehaviorService
{
    /// <summary>Raised after preferences have been saved successfully; may run on any thread.</summary>
    event Action<BehaviorSettings>? SettingsChanged;

    Task<(SavedCoordinatorModel Coordinator, SavedUserModel User)?> GetAutoConnectInstance();
    Task SetLastIdentityAsync((string CoordinatorId, int UserId) identity);
    Task RemoveLastIdentityAsync();
    
    Task<BehaviorSettings> GetSettingsAsync();
    Task SaveSettingsAsync(BehaviorSettings settings);
}
