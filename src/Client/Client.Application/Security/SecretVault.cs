using Client.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Client.Application.Security;

/// <summary>
/// Keeps secrets in the system keyring, and in the local database when the system has none or it fails. A keyring
/// that fails once is not asked again until the application restarts.
/// </summary>
public sealed partial class SecretVault(ISecureStorage keyring, ISecretsRepository database, ILogger<SecretVault> logger)
    : ISecretVault
{
    private volatile bool _isKeyringBroken;
    private bool? _isDatabaseEncrypted;

    private bool UsesKeyring => keyring.IsAvailable && !_isKeyringBroken;

    public bool IsProtected => UsesKeyring || _isDatabaseEncrypted == true;

    public async Task SaveAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        await RefreshDatabaseStateAsync();
        if (UsesKeyring)
        {
            try
            {
                await keyring.SavePassword(value, name, cancellationToken);
                // A copy kept while the keyring was failing is not needed any more
                await database.DeleteAsync(name);
                return;
            }
            catch (SecureStorageException exception)
            {
                OnKeyringFailed(exception);
            }
        }

        await database.SaveAsync(name, value);
    }

    public async Task<string?> LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        await RefreshDatabaseStateAsync();
        if (UsesKeyring)
        {
            try
            {
                if (await keyring.LoadPassword(name, cancellationToken) is { } value)
                    return value;
            }
            catch (SecureStorageException exception)
            {
                OnKeyringFailed(exception);
            }
        }

        return await database.LoadAsync(name);
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        if (UsesKeyring)
        {
            try
            {
                await keyring.DeletePassword(name, cancellationToken);
            }
            catch (SecureStorageException exception)
            {
                OnKeyringFailed(exception);
            }
        }

        await database.DeleteAsync(name);
    }

    private async Task RefreshDatabaseStateAsync()
    {
        try
        {
            _isDatabaseEncrypted = await database.IsDatabaseEncryptedAsync();
        }
        catch (Exception exception)
        {
            LogDatabaseStateUnknown(exception);
        }
    }

    private void OnKeyringFailed(SecureStorageException exception)
    {
        _isKeyringBroken = true;
        LogKeyringFailed(exception, exception.Error);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The system keyring failed ({Error}); secrets are kept in the local database until the restart.")]
    private partial void LogKeyringFailed(Exception exception, SecureStorageError error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not tell whether the local database is encrypted.")]
    private partial void LogDatabaseStateUnknown(Exception exception);
}
