using Client.Core.Settings;


namespace Client.Application.Ports.Repositories;

public interface IBehaviorSettingsRepository
{
    Task<BehaviorSettings> GetAsync();
    Task SaveAsync(BehaviorSettings settings);
    
    Task<(string CoordinatorId, int UserId)?> GetAutoConnectInstanceAsync();
    Task SetAutoConnectInstanceAsync((string CoordinatorId, int UserId)? identity);
}
