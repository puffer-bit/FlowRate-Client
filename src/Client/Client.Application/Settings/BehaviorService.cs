using System.Threading.Tasks;
using Client.Core.Settings;
using Client.Application.Ports.Repositories;

namespace Client.Application.Settings;

public class BehaviorService : IBehaviorService
{
    private readonly IBehaviorSettingsRepository _behaviorSettingsRepository;
    private readonly IAccountingSettingsRepository _accountingSettingsRepository;
    
    public BehaviorService(IBehaviorSettingsRepository behaviorSettingsRepository, 
        IAccountingSettingsRepository accountingSettingsRepository)
    {
        _behaviorSettingsRepository = behaviorSettingsRepository;
        _accountingSettingsRepository = accountingSettingsRepository;
    }

    public async Task<(SavedCoordinatorModel Coordinator, SavedUserModel User)?> GetAutoConnectInstance()
    {
        var identityPairIds = await _behaviorSettingsRepository.GetAutoConnectInstanceAsync();

        if (identityPairIds != null)
        {
            var identityPair = await _accountingSettingsRepository.GetCoordinatorInstanceAsync(
                identityPairIds.Value);

            if (identityPair != null)
                return identityPair.Value;
        }
        
        return null;
    }

    public async Task<BehaviorSettings> GetSettingsAsync()
    {
        return await _behaviorSettingsRepository.GetAsync();
    }

    public async Task SaveSettingsAsync(BehaviorSettings settings)
    {
        await _behaviorSettingsRepository.SaveAsync(settings);
    }
    
    public async Task SetLastIdentityAsync((string CoordinatorId, int UserId) lastIdentity)
    {
        var savedCoordinator = await _accountingSettingsRepository.GetCoordinatorAsync(lastIdentity.CoordinatorId);
        
        if (savedCoordinator != null)
        {
            var savedCoordinatorInstanceIds = await _accountingSettingsRepository.GetCoordinatorInstanceAsync
                ((savedCoordinator.CoordinatorId, lastIdentity.UserId));
            
            if (savedCoordinatorInstanceIds != null)
                await _behaviorSettingsRepository.SetAutoConnectInstanceAsync((savedCoordinator.CoordinatorId, 
                    lastIdentity.UserId));
            
            return;
        }

        throw new ArgumentException($"{nameof(savedCoordinator)} does not exist in database");
    }
    
    public async Task RemoveLastIdentityAsync()
    {
        await _behaviorSettingsRepository.SetAutoConnectInstanceAsync(null);
    }
}