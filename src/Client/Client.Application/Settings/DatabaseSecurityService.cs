using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Client.Application.Settings;

public sealed partial class DatabaseSecurityService : IDatabaseSecurityService
{
    // The account of the keyring entry; the keyring tells the application apart itself
    private const string KeyringAccount = "database";

    private readonly IDatabaseProtection _protection;
    private readonly ISecureStorage _keyring;
    private readonly IAccountingSettingsRepository _settings;
    private readonly ILogger<DatabaseSecurityService> _logger;

    // Kept for the keyring: it may be asked to keep the password later
    private string? _password;
    private bool _isUnlockedFromKeyring;

    public DatabaseSecurityService(IDatabaseProtection protection, ISecureStorage keyring,
        IAccountingSettingsRepository settings, ILogger<DatabaseSecurityService> logger)
    {
        _protection = protection;
        _keyring = keyring;
        _settings = settings;
        _logger = logger;
    }

    public bool IsEncrypted => _protection.IsEncrypted;

    public bool CanStorePassword => _keyring.IsAvailable;

    public bool CanLockFile => _protection.CanLockFile;

    public async Task<DatabaseUnlockResult> UnlockWithStoredPasswordAsync()
    {
        if (!_protection.IsEncrypted)
            return DatabaseUnlockResult.Unlocked;

        if (!_keyring.IsAvailable)
            return DatabaseUnlockResult.PasswordNeeded;

        string? stored;

        try
        {
            stored = await _keyring.LoadPassword(KeyringAccount);
        }
        catch (SecureStorageException exception)
        {
            LogKeyringReadFailed(exception);
            return DatabaseUnlockResult.PasswordNeeded;
        }

        if (stored == null)
            return DatabaseUnlockResult.PasswordNeeded;

        var result = await UnlockAsync(stored);
        _isUnlockedFromKeyring = result == DatabaseUnlockResult.Unlocked;

        // The password was changed on another copy of the database, or the entry is someone else's
        return result == DatabaseUnlockResult.WrongPassword ? DatabaseUnlockResult.PasswordNeeded : result;
    }

    public async Task<DatabaseUnlockResult> UnlockAsync(string password)
    {
        try
        {
            if (!await _protection.UnlockAsync(password))
                return DatabaseUnlockResult.WrongPassword;
        }
        catch (DatabaseInUseException)
        {
            return DatabaseUnlockResult.InUse;
        }

        _password = password;
        return DatabaseUnlockResult.Unlocked;
    }

    public async Task ApplyAsync()
    {
        var settings = await _settings.GetDatabaseSettingsAsync();

        // The file is the truth: a copy of the database moved over this one brings its own state
        if (settings.IsDatabaseEncryptionEnabled != _protection.IsEncrypted)
        {
            settings.IsDatabaseEncryptionEnabled = _protection.IsEncrypted;
            settings.IsDatabasePasswordSaveEnabled &= _protection.IsEncrypted;
            await _settings.UpdateDatabaseSettingsAsync(settings);
        }

        // The keyring lost the entry, e.g. it was reset: the password just typed goes back there
        if (settings.IsDatabasePasswordSaveEnabled && !_isUnlockedFromKeyring && _password != null && _keyring.IsAvailable)
        {
            try
            {
                await _keyring.SavePassword(_password, KeyringAccount);
            }
            catch (SecureStorageException exception)
            {
                LogKeyringWriteFailed(exception);
            }
        }

        if (settings.IsDatabaseLockEnabled && _protection.CanLockFile)
            _protection.LockFile();
    }

    public async Task<DatabaseSettings> GetSettingsAsync()
    {
        var settings = await _settings.GetDatabaseSettingsAsync();
        settings.IsDatabaseEncryptionEnabled = _protection.IsEncrypted;
        return settings;
    }

    public async Task SetPasswordAsync(string password)
    {
        await _protection.ChangePasswordAsync(password);
        _password = password;

        var settings = await _settings.GetDatabaseSettingsAsync();
        settings.IsDatabaseEncryptionEnabled = true;

        try
        {
            if (settings.IsDatabasePasswordSaveEnabled)
                await _keyring.SavePassword(password, KeyringAccount);
        }
        catch (SecureStorageException)
        {
            // The keyring keeps the old password, which no longer opens the database
            settings.IsDatabasePasswordSaveEnabled = false;
            await ForgetStoredPasswordAsync();
            throw;
        }
        finally
        {
            await _settings.UpdateDatabaseSettingsAsync(settings);
        }
    }

    public async Task DecryptAsync()
    {
        await _protection.ChangePasswordAsync(null);
        _password = null;

        var settings = await _settings.GetDatabaseSettingsAsync();
        settings.IsDatabaseEncryptionEnabled = false;
        settings.IsDatabasePasswordSaveEnabled = false;
        await _settings.UpdateDatabaseSettingsAsync(settings);

        await ForgetStoredPasswordAsync();
    }

    public async Task SetPasswordStoredAsync(bool isStored)
    {
        if (isStored)
        {
            if (_password == null)
                throw new InvalidOperationException("The database is not encrypted, there is no password to keep");

            await _keyring.SavePassword(_password, KeyringAccount);
        }
        else
        {
            await _keyring.DeletePassword(KeyringAccount);
        }

        var settings = await _settings.GetDatabaseSettingsAsync();
        settings.IsDatabasePasswordSaveEnabled = isStored;
        await _settings.UpdateDatabaseSettingsAsync(settings);
    }

    public async Task SetFileLockedAsync(bool isLocked)
    {
        var settings = await _settings.GetDatabaseSettingsAsync();
        settings.IsDatabaseLockEnabled = isLocked;
        await _settings.UpdateDatabaseSettingsAsync(settings);
    }

    private async Task ForgetStoredPasswordAsync()
    {
        if (!_keyring.IsAvailable)
            return;

        try
        {
            await _keyring.DeletePassword(KeyringAccount);
        }
        catch (SecureStorageException exception)
        {
            LogKeyringWriteFailed(exception);
        }
    }

    [LoggerMessage(LogLevel.Warning, "The database password could not be read from the keyring")]
    private partial void LogKeyringReadFailed(Exception exception);

    [LoggerMessage(LogLevel.Warning, "The database password could not be written to the keyring")]
    private partial void LogKeyringWriteFailed(Exception exception);
}
