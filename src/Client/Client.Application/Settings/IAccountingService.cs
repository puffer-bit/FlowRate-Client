using System.Threading.Tasks;
using Client.Core.Coordinator;
using Client.Core.Settings;

namespace Client.Application.Settings;

public interface IAccountingService
{
    Task<IReadOnlyList<SavedCoordinatorModel>> GetSavedCoordinatorsAsync(string address, int port);
    Task RemoveUserIdentityAsync(string coordinatorId, int userId);
    Task<IReadOnlyList<SavedCoordinatorModel>> GetAllSavedCoordinatorsAsync();
    Task SaveCoordinatorInstanceAsync(
        SavedCoordinatorModel instanceModel, SavedUserModel userModel);
    Task RemoveCoordinatorInstanceAsync(string coordinatorId);

    /// <summary>Keeps the account password with a saved identity, or forgets it when null.</summary>
    Task SetAccountPasswordAsync(string coordinatorId, int userId, string? accountPassword);

    Task<IReadOnlyList<ServerCertificateModel>> GetTrustedCertificatesAsync();

    /// <summary>Forgets the certificate trusted for the address; the next connection asks again.</summary>
    Task RemoveTrustedCertificateAsync(string host, int port);
}