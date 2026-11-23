using System.Collections.Concurrent;
using System.Security.Cryptography;
using Client.Application.Ports;

namespace Client.Application.Security;

/// <summary>
/// Device keys made in software and kept in the <see cref="ISecretVault"/>: the system keyring, or the local
/// database where there is none. Platforms with a hardware key store use their own.
/// </summary>
public sealed class VaultDeviceKeyStore(ISecretVault vault) : IDeviceKeyStore
{
    public async Task<byte[]> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = key.ExportPkcs8PrivateKey();
        try
        {
            await vault.SaveAsync(SecretNames.DeviceKey(name), Convert.ToBase64String(privateKey), cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }

        return key.ExportSubjectPublicKeyInfo();
    }

    public async Task<byte[]?> SignAsync(string name, byte[] data, CancellationToken cancellationToken = default)
    {
        if (await vault.LoadAsync(SecretNames.DeviceKey(name), cancellationToken) is not { } stored)
            return null;

        var privateKey = Convert.FromBase64String(stored);
        try
        {
            using var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(privateKey, out _);
            return Sign(key, data);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default) =>
        vault.DeleteAsync(SecretNames.DeviceKey(name), cancellationToken);

    public static byte[] Sign(ECDsa key, byte[] data) =>
        key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
}

/// <summary>
/// Keys that live as long as the process: those of temporary logins, which are never written anywhere, and the
/// default of a session made without a key store.
/// </summary>
public sealed class InMemoryDeviceKeyStore : IDeviceKeyStore
{
    private readonly ConcurrentDictionary<string, ECDsa> _keys = new();

    public Task<byte[]> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (_keys.TryRemove(name, out var previous))
            previous.Dispose();
        _keys[name] = key;
        return Task.FromResult(key.ExportSubjectPublicKeyInfo());
    }

    public Task<byte[]?> SignAsync(string name, byte[] data, CancellationToken cancellationToken = default) =>
        Task.FromResult(_keys.TryGetValue(name, out var key) ? VaultDeviceKeyStore.Sign(key, data) : null);

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_keys.TryRemove(name, out var key))
            key.Dispose();
        return Task.CompletedTask;
    }
}
