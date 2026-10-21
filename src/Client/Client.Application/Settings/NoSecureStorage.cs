using Client.Application.Ports;

namespace Client.Application.Settings;

/// <summary>
/// The keyring of a platform that has none, or whose one is not wired up yet.
/// </summary>
internal sealed class NoSecureStorage : ISecureStorage
{
    public bool IsAvailable => false;

    public Task SavePassword(string password, string username, CancellationToken cancellationToken = default) =>
        throw Unavailable();

    public Task<string?> LoadPassword(string username, CancellationToken cancellationToken = default) =>
        throw Unavailable();

    public Task DeletePassword(string username, CancellationToken cancellationToken = default) =>
        throw Unavailable();

    public void Dispose()
    {
    }

    private static SecureStorageException Unavailable() =>
        new(SecureStorageError.StorageUnavailable, "This system has no keyring");
}
