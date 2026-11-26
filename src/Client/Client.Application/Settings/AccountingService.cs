using System.Threading.Tasks;
using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Application.Security;
using Client.Core.Coordinator;
using Client.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Client.Application.Settings;

public sealed partial class AccountingService : IAccountingService
{
    private readonly IAccountingSettingsRepository _repository;
    private readonly ITrustedCertificatesRepository _trustedCertificatesRepository;
    private readonly ISecretVault _vault;
    private readonly IDeviceKeyStore _deviceKeys;
    private readonly ILogger<AccountingService> _logger;

    public AccountingService(
        IAccountingSettingsRepository accountingSettingsRepository,
        ITrustedCertificatesRepository trustedCertificatesRepository,
        ISecretVault vault,
        IDeviceKeyStore deviceKeys,
        ILogger<AccountingService> logger)
    {
        _repository = accountingSettingsRepository;
        _trustedCertificatesRepository = trustedCertificatesRepository;
        _vault = vault;
        _deviceKeys = deviceKeys;
        _logger = logger;
    }

    public bool AreSecretsProtected => _vault.IsProtected;

    public Task<IReadOnlyList<ServerCertificateModel>> GetTrustedCertificatesAsync() =>
        _trustedCertificatesRepository.GetAllAsync();

    public Task RemoveTrustedCertificateAsync(string host, int port) =>
        _trustedCertificatesRepository.RemoveAsync(host, port);

    public async Task SaveCoordinatorInstanceAsync(SavedCoordinatorModel instanceModel, SavedUserModel userModel)
    {
        await _repository.SaveCoordinatorAsync(instanceModel);
        await _repository.SaveUserAsync(userModel);

        var serverPassword = SecretNames.ServerPassword(userModel.CoordinatorId, userModel.Id);
        if (string.IsNullOrEmpty(userModel.Password))
            await _vault.DeleteAsync(serverPassword);
        else
            await _vault.SaveAsync(serverPassword, userModel.Password);

        if (userModel.RecoveryPhrase != null)
        {
            await _vault.SaveAsync(SecretNames.RecoveryPhrase(userModel.CoordinatorId, userModel.Id),
                userModel.RecoveryPhrase);
            await KeepBackupKeyAsync(userModel.CoordinatorId, userModel.Id, userModel.RecoveryPhrase);
        }
        else if (userModel.BackupKey != null)
        {
            await _vault.SaveAsync(SecretNames.BackupKey(userModel.CoordinatorId, userModel.Id), userModel.BackupKey);
        }
    }

    public async Task<IReadOnlyList<SavedCoordinatorModel>> GetSavedCoordinatorsAsync(string address, int port)
    {
        var coordinators = await GetAllSavedCoordinatorsAsync();
        return coordinators.Where(c => string.Equals(c.Address, address, StringComparison.OrdinalIgnoreCase)
            && c.Port == port).ToList();
    }

    public async Task<IReadOnlyList<SavedCoordinatorModel>> GetAllSavedCoordinatorsAsync()
    {
        var coordinators = await _repository.GetAllCoordinatorsAsync();
        foreach (var coordinator in coordinators)
        {
            var users = new List<SavedUserModel>(coordinator.Users.Count);
            foreach (var user in coordinator.Users)
                users.Add(await WithSecretsAsync(user));
            coordinator.Users.Clear();
            coordinator.Users.AddRange(users);
        }

        return coordinators;
    }

    public async Task RemoveUserIdentityAsync(string coordinatorId, int userId)
    {
        var saved = await _repository.GetCoordinatorInstanceAsync((coordinatorId, userId));
        await ForgetSecretsAsync(coordinatorId, userId, saved?.User.DeviceKey);
        await _repository.DeleteUserAsync(coordinatorId, userId);
    }

    public async Task RemoveCoordinatorInstanceAsync(string coordinatorId)
    {
        if (await _repository.GetCoordinatorAsync(coordinatorId) is { } coordinator)
        {
            foreach (var user in coordinator.Users)
                await ForgetSecretsAsync(coordinatorId, user.Id, user.DeviceKey);
        }

        await _repository.DeleteCoordinatorAsync(coordinatorId);
    }

    public async Task ForgetDeviceAsync(string coordinatorId, int userId, LoginLostReason reason)
    {
        var saved = await _repository.GetCoordinatorInstanceAsync((coordinatorId, userId));
        if (saved?.User.DeviceKey is { } key)
            await DeleteDeviceKeyAsync(key);
        await _repository.SetDeviceAsync(coordinatorId, userId, null, null, reason);
    }

    public async Task KeepRecoveryPhraseAsync(string coordinatorId, int userId, string phrase)
    {
        await _vault.SaveAsync(SecretNames.RecoveryPhrase(coordinatorId, userId), phrase);
        await KeepBackupKeyAsync(coordinatorId, userId, phrase);
        await _repository.SetRecoveryPhraseSavedAsync(coordinatorId, userId, false);
    }

    // Backups are encrypted with the key the phrase derives, so this device keeps making them once the phrase is gone
    private async Task KeepBackupKeyAsync(string coordinatorId, int userId, string phrase)
    {
        using var keys = RecoveryKeySet.FromPhrase(phrase);
        if (keys != null)
            await _vault.SaveAsync(SecretNames.BackupKey(coordinatorId, userId), Convert.ToBase64String(keys.BackupKey));
    }

    public Task<string?> GetRecoveryPhraseAsync(string coordinatorId, int userId) =>
        _vault.LoadAsync(SecretNames.RecoveryPhrase(coordinatorId, userId));

    public async Task ConfirmRecoveryPhraseSavedAsync(string coordinatorId, int userId, bool keepOnDevice)
    {
        if (!keepOnDevice)
            await _vault.DeleteAsync(SecretNames.RecoveryPhrase(coordinatorId, userId));
        await _repository.SetRecoveryPhraseSavedAsync(coordinatorId, userId, true);
    }

    /// <summary>
    /// The account with its secrets from the vault. A server password the earlier client kept in the database moves to
    /// the vault on the way.
    /// </summary>
    private async Task<SavedUserModel> WithSecretsAsync(SavedUserModel user)
    {
        var name = SecretNames.ServerPassword(user.CoordinatorId, user.Id);
        var password = user.Password;
        try
        {
            if (!string.IsNullOrEmpty(password))
            {
                await _vault.SaveAsync(name, password);
                await _repository.ClearServerPasswordAsync(user.CoordinatorId, user.Id);
            }
            else
            {
                password = await _vault.LoadAsync(name);
            }
        }
        catch (Exception exception)
        {
            LogSecretsNotRead(exception, user.Id);
        }

        string? phrase = null;
        try
        {
            phrase = await _vault.LoadAsync(SecretNames.RecoveryPhrase(user.CoordinatorId, user.Id));
        }
        catch (Exception exception)
        {
            LogSecretsNotRead(exception, user.Id);
        }

        return new SavedUserModel
        {
            Id = user.Id,
            CoordinatorLabel = user.CoordinatorLabel,
            Username = user.Username,
            CoordinatorId = user.CoordinatorId,
            Password = password,
            DeviceId = user.DeviceId,
            DeviceKey = user.DeviceKey,
            RecoveryPhrase = phrase,
            IsRecoveryPhraseSaved = user.IsRecoveryPhraseSaved,
            UserIdentity = user.UserIdentity,
            SessionToken = user.SessionToken,
            AccountPassword = user.AccountPassword,
            LoginLostReason = user.LoginLostReason,
            AvatarUrl = user.AvatarUrl
        };
    }

    private async Task ForgetSecretsAsync(string coordinatorId, int userId, string? deviceKey)
    {
        await _vault.DeleteAsync(SecretNames.ServerPassword(coordinatorId, userId));
        await _vault.DeleteAsync(SecretNames.RecoveryPhrase(coordinatorId, userId));
        await _vault.DeleteAsync(SecretNames.BackupKey(coordinatorId, userId));
        if (deviceKey != null)
            await DeleteDeviceKeyAsync(deviceKey);
    }

    private async Task DeleteDeviceKeyAsync(string key)
    {
        try
        {
            await _deviceKeys.DeleteAsync(key);
        }
        catch (Exception exception)
        {
            LogDeviceKeyNotDeleted(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The secrets of saved account {UserId} could not be read.")]
    private partial void LogSecretsNotRead(Exception exception, int userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The key of a forgotten device could not be deleted.")]
    private partial void LogDeviceKeyNotDeleted(Exception exception);
}
