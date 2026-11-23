namespace Client.Application.Ports;

/// <summary>
/// Where the client keeps its secrets: the server passwords, device keys of software key stores and unsaved recovery
/// phrases. The system keyring when there is one; the local database otherwise, which is only as safe as its own
/// encryption.
/// </summary>
public interface ISecretVault
{
    /// <summary>Secrets reach neither a copy of the data folder nor other users: the keyring, or an encrypted database.</summary>
    bool IsProtected { get; }

    Task SaveAsync(string name, string value, CancellationToken cancellationToken = default);
    Task<string?> LoadAsync(string name, CancellationToken cancellationToken = default);
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>The table of the local database secrets fall back to when the system has no keyring.</summary>
public interface ISecretsRepository
{
    Task SaveAsync(string name, string value);
    Task<string?> LoadAsync(string name);
    Task DeleteAsync(string name);

    /// <summary>The database file is encrypted with a password.</summary>
    Task<bool> IsDatabaseEncryptedAsync();
}
