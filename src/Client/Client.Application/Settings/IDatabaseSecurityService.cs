using Client.Application.Ports;
using Client.Core.Settings;

namespace Client.Application.Settings;

public enum DatabaseUnlockResult
{
    Unlocked,

    /// <summary>The file is encrypted and no stored password opens it: the user has to type one.</summary>
    PasswordNeeded,

    WrongPassword,

    /// <summary>Another process, e.g. a second copy of the application, holds the file locked.</summary>
    InUse
}

/// <summary>
/// The encryption of the local database, the keyring that keeps its password and the lock of its file.
/// </summary>
public interface IDatabaseSecurityService
{
    bool IsEncrypted { get; }

    /// <summary>Whether the system has a keyring to keep the password in.</summary>
    bool CanStorePassword { get; }

    /// <summary>Whether the system can hold the file locked against other processes.</summary>
    bool CanLockFile { get; }

    /// <summary>
    /// Called at start, before anything reads the database: a plain file needs nothing, an encrypted
    /// one is opened with the password from the keyring if it is kept there.
    /// </summary>
    Task<DatabaseUnlockResult> UnlockWithStoredPasswordAsync();

    /// <summary>Opens the encrypted file with the password the user typed.</summary>
    Task<DatabaseUnlockResult> UnlockAsync(string password);

    /// <summary>
    /// Called at start once the schema is current: puts the stored settings into effect.
    /// </summary>
    Task ApplyAsync();

    /// <summary>The settings, with the encryption as the file on the disk has it.</summary>
    Task<DatabaseSettings> GetSettingsAsync();

    /// <summary>
    /// Encrypts the database with <paramref name="password"/>, or changes the password of an
    /// encrypted one; the keyring gets the new password if it keeps one.
    /// </summary>
    /// <exception cref="SecureStorageException">
    /// The database is encrypted, but the keyring did not take the password: keeping it there is off.
    /// </exception>
    Task SetPasswordAsync(string password);

    /// <summary>Decrypts the database and forgets its password, the keyring too.</summary>
    Task DecryptAsync();

    /// <summary>Puts the password of the encrypted database into the keyring or takes it out.</summary>
    /// <exception cref="SecureStorageException">The keyring refused; the setting stays as it was.</exception>
    Task SetPasswordStoredAsync(bool isStored);

    /// <summary>Whether the file is held locked from the next start on.</summary>
    Task SetFileLockedAsync(bool isLocked);
}
