namespace Client.Application.Ports;

/// <summary>
/// The P-256 keys devices log in with. A platform keeps them where it can, out of reach of a copy of the data folder:
/// a hardware key store when there is one, the system keyring otherwise.
/// </summary>
public interface IDeviceKeyStore
{
    /// <summary>Makes a new key under <paramref name="name"/>; returns its public key (SubjectPublicKeyInfo DER).</summary>
    Task<byte[]> CreateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs <paramref name="data"/> (SHA-256, IEEE P1363 r‖s); null when the key is gone, e.g. the keyring was reset.
    /// </summary>
    Task<byte[]?> SignAsync(string name, byte[] data, CancellationToken cancellationToken = default);

    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}
