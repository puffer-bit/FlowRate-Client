using Client.Core.Settings;


namespace Client.Application.Ports.Repositories;

public interface IBehaviorSettingsRepository
{
    Task<BehaviorSettings> GetAsync();
    Task SaveAsync(BehaviorSettings settings);
    
    Task<AutoConnectChoice> GetAutoConnectChoiceAsync();
    Task SetAutoConnectChoiceAsync(AutoConnectChoice choice);

    /// <summary>The server and account of the last successful connection; the account is unknown for older ones.</summary>
    Task<(string CoordinatorId, int? UserId)?> GetLastConnectionAsync();
}
