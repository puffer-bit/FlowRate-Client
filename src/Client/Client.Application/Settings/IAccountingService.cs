using System.Threading.Tasks;
using Client.Core.Coordinator;
using Client.Core.Settings;

namespace Client.Application.Settings;

/// <summary>
/// The servers and accounts remembered on this device. Their secrets (server passwords, device keys, unsaved recovery
/// phrases) are kept in the secret vault: the system keyring, or the local database where there is none.
/// </summary>
public interface IAccountingService
{
    Task<IReadOnlyList<SavedCoordinatorModel>> GetSavedCoordinatorsAsync(string address, int port);
    Task<IReadOnlyList<SavedCoordinatorModel>> GetAllSavedCoordinatorsAsync();

    /// <summary>Saves the server and the account, with the secrets the account model carries.</summary>
    Task SaveCoordinatorInstanceAsync(SavedCoordinatorModel instanceModel, SavedUserModel userModel);

    /// <summary>Forgets the account on this device, with its secrets and its device key.</summary>
    Task RemoveUserIdentityAsync(string coordinatorId, int userId);

    Task RemoveCoordinatorInstanceAsync(string coordinatorId);

    /// <summary>The device of the account ended: its key goes, and the account needs a login.</summary>
    Task ForgetDeviceAsync(string coordinatorId, int userId);

    /// <summary>Keeps a new recovery phrase of the account until the user confirms saving it.</summary>
    Task KeepRecoveryPhraseAsync(string coordinatorId, int userId, string phrase);

    /// <summary>The recovery phrase while it is kept on this device; null otherwise.</summary>
    Task<string?> GetRecoveryPhraseAsync(string coordinatorId, int userId);

    /// <summary>
    /// The user saved the recovery phrase: it is forgotten on this device unless <paramref name="keepOnDevice"/>.
    /// </summary>
    Task ConfirmRecoveryPhraseSavedAsync(string coordinatorId, int userId, bool keepOnDevice);

    /// <summary>Secrets reach neither a copy of the data folder nor other users: the keyring, or an encrypted database.</summary>
    bool AreSecretsProtected { get; }

    Task<IReadOnlyList<ServerCertificateModel>> GetTrustedCertificatesAsync();

    /// <summary>Forgets the certificate trusted for the address; the next connection asks again.</summary>
    Task RemoveTrustedCertificateAsync(string host, int port);
}
