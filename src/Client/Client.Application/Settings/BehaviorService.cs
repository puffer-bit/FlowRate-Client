using System.Threading.Tasks;
using Client.Core.Settings;
using Client.Application.Ports.Repositories;

namespace Client.Application.Settings;

public class BehaviorService : IBehaviorService
{
    public event Action<BehaviorSettings>? SettingsChanged;

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
        var choice = await _behaviorSettingsRepository.GetAutoConnectChoiceAsync();
        var last = await _behaviorSettingsRepository.GetLastConnectionAsync();

        // A picked server that is gone gives way to the last one
        var coordinator = choice.CoordinatorId is { } picked
            ? await _accountingSettingsRepository.GetCoordinatorAsync(picked)
            : null;
        var isPicked = coordinator != null;
        if (coordinator == null && last is { CoordinatorId: var lastCoordinatorId })
            coordinator = await _accountingSettingsRepository.GetCoordinatorAsync(lastCoordinatorId);

        if (coordinator == null || coordinator.Users.Count == 0)
            return null;

        // The account picked, then the last one of the server, then one that can log in without asking
        int? userId = isPicked && choice.UserId != null ? choice.UserId
            : coordinator.LastUserId ?? (last?.CoordinatorId == coordinator.CoordinatorId ? last?.UserId : null);
        var user = coordinator.Users.FirstOrDefault(account => account.Id == userId)
                   ?? coordinator.Users.FirstOrDefault(account => account.CanLogIn)
                   ?? coordinator.Users[0];

        return (coordinator, user);
    }

    public Task<AutoConnectChoice> GetAutoConnectChoiceAsync() =>
        _behaviorSettingsRepository.GetAutoConnectChoiceAsync();

    public Task SetAutoConnectChoiceAsync(AutoConnectChoice choice) =>
        _behaviorSettingsRepository.SetAutoConnectChoiceAsync(choice);

    public Task RecordConnectionAsync(string coordinatorId, int userId) =>
        _accountingSettingsRepository.SetLastConnectionAsync(coordinatorId, userId);

    public async Task<BehaviorSettings> GetSettingsAsync()
    {
        return await _behaviorSettingsRepository.GetAsync();
    }

    public async Task SaveSettingsAsync(BehaviorSettings settings)
    {
        await _behaviorSettingsRepository.SaveAsync(settings);
        SettingsChanged?.Invoke(settings);
    }
}
