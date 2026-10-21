namespace Client.Application.Ports;

/// <summary>
/// The encryption of the local database file and its lock against other processes.
/// </summary>
public interface IDatabaseProtection
{
    /// <summary>Whether the file on the disk is encrypted, so it opens only with its password.</summary>
    bool IsEncrypted { get; }

    /// <summary>Whether this system can hold the file locked, see <see cref="LockFile"/>.</summary>
    bool CanLockFile { get; }

    /// <summary>
    /// Opens the encrypted file with <paramref name="password"/> from now on.
    /// </summary>
    /// <returns>False when the password does not open it; nothing changes then.</returns>
    /// <exception cref="DatabaseInUseException">Another process holds the file locked.</exception>
    Task<bool> UnlockAsync(string password);

    /// <summary>
    /// Encrypts the file with <paramref name="password"/>, encrypts it again with a new one, or
    /// decrypts it with null. Takes a moment: every page is written anew.
    /// </summary>
    /// <remarks>The copies made before schema upgrades are deleted: they keep the old encryption.</remarks>
    Task ChangePasswordAsync(string? password);

    /// <summary>
    /// Holds the file locked against other processes until the application exits. Called at start,
    /// before the repositories are used.
    /// </summary>
    void LockFile();
}

/// <summary>Another process, e.g. a second copy of the application, holds the database file locked.</summary>
public sealed class DatabaseInUseException(Exception? inner = null)
    : Exception("The database file is locked by another process", inner);
