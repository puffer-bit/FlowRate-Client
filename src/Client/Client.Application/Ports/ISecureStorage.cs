namespace Client.Application.Ports;

/// <summary>
/// The secrets keyring of the system, e.g. the Secret Service on Linux.
/// </summary>
public interface ISecureStorage : IDisposable
{
    /// <summary>
    /// Whether this system has a keyring at all. The calls of one that has none throw
    /// <see cref="SecureStorageException"/> with <see cref="SecureStorageError.StorageUnavailable"/>.
    /// </summary>
    bool IsAvailable { get; }

    Task SavePassword(string password, string username, CancellationToken cancellationToken = default);
    Task<string?> LoadPassword(string username, CancellationToken cancellationToken = default);
    Task DeletePassword(string username, CancellationToken cancellationToken = default);
}

public enum SecureStorageError
{
    StorageUnavailable,
    StorageFailed
}

public sealed class SecureStorageException(SecureStorageError error, string details, Exception? inner = null)
    : Exception(details, inner)
{
    public SecureStorageError Error { get; } = error;
}
