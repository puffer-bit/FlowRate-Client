using System.Threading.Tasks;
using Client.Core.Coordinator;
using Client.Core.Settings;

namespace Client.Application.Settings;

public interface IAccountingService
{
    Task<SavedCoordinatorModel?> GetSavedCoordinator(
        string address, 
        int port);
    Task<IReadOnlyList<SavedCoordinatorModel>> GetAllSavedCoordinatorsAsync();
    Task SaveCoordinatorInstanceAsync(
        SavedCoordinatorModel instanceModel, SavedUserModel userModel);
    Task RemoveCoordinatorInstanceAsync(string coordinatorId);

    Task<IReadOnlyList<ServerCertificateModel>> GetTrustedCertificatesAsync();

    /// <summary>Forgets the certificate trusted for the address; the next connection asks again.</summary>
    Task RemoveTrustedCertificateAsync(string host, int port);
}