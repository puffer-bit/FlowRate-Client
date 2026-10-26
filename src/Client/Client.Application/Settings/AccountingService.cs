using System.Threading.Tasks;
using Client.Core.Coordinator;
using Client.Core.Settings;
using Client.Application.Ports.Repositories;

namespace Client.Application.Settings;

public class AccountingService : IAccountingService
{
    private readonly IAccountingSettingsRepository _accountingSettingsRepository;
    private readonly ITrustedCertificatesRepository _trustedCertificatesRepository;

    public AccountingService(
        IAccountingSettingsRepository accountingSettingsRepository,
        ITrustedCertificatesRepository trustedCertificatesRepository)
    {
        _accountingSettingsRepository = accountingSettingsRepository;
        _trustedCertificatesRepository = trustedCertificatesRepository;
    }

    public Task<IReadOnlyList<ServerCertificateModel>> GetTrustedCertificatesAsync() =>
        _trustedCertificatesRepository.GetAllAsync();

    public Task RemoveTrustedCertificateAsync(string host, int port) =>
        _trustedCertificatesRepository.RemoveAsync(host, port);

    public async Task SaveCoordinatorInstanceAsync(SavedCoordinatorModel instanceModel, SavedUserModel userModel)
    {
        await _accountingSettingsRepository.SaveCoordinatorAsync(instanceModel);
        await _accountingSettingsRepository.SaveUserAsync(userModel);
    }

    public async Task<SavedCoordinatorModel?> GetSavedCoordinator(
        string address, 
        int port)
    {
        return await _accountingSettingsRepository.GetCoordinatorAsync(address, port);
    }

    public async Task<IReadOnlyList<SavedCoordinatorModel>> GetAllSavedCoordinatorsAsync()
    {
        return await _accountingSettingsRepository.GetAllCoordinatorsAsync();
    }
    
    public Task SetAccountPasswordAsync(string coordinatorId, int userId, string? accountPassword) =>
        _accountingSettingsRepository.SetAccountPasswordAsync(coordinatorId, userId, accountPassword);

    public async Task RemoveCoordinatorInstanceAsync(string coordinatorId)
    {
        await _accountingSettingsRepository.DeleteCoordinatorAsync(coordinatorId);
    }
}