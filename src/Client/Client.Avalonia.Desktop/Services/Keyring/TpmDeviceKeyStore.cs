using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Client.Application.Ports;
using Client.Application.Security;

namespace Client.Services.Keyring;

/// <summary>
/// Device keys in the TPM of Windows, through the Platform Crypto Provider: made inside the chip and never exportable.
/// A machine without a usable TPM keeps them in software under DPAPI instead.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TpmDeviceKeyStore(ISecretVault vault) : IDeviceKeyStore
{
    private static readonly CngProvider PlatformProvider = new("Microsoft Platform Crypto Provider");
    private const string KeyPrefix = "FlowRate-device-";

    private readonly VaultDeviceKeyStore _software = new(vault);

    public Task<byte[]> CreateAsync(string name, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        try
        {
            var parameters = new CngKeyCreationParameters
            {
                Provider = PlatformProvider,
                ExportPolicy = CngExportPolicies.None,
                KeyCreationOptions = CngKeyCreationOptions.None
            };
            using var key = CngKey.Create(CngAlgorithm.ECDsaP256, KeyPrefix + name, parameters);
            using var ecdsa = new ECDsaCng(key);
            return ecdsa.ExportSubjectPublicKeyInfo();
        }
        catch (CryptographicException)
        {
            // No TPM, a TPM without ECC, or one the policy keeps from the app
            return await _software.CreateAsync(name, cancellationToken);
        }
    }, cancellationToken);

    public Task<byte[]?> SignAsync(string name, byte[] data, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        if (!IsInTpm(name))
            return await _software.SignAsync(name, data, cancellationToken);

        using var key = CngKey.Open(KeyPrefix + name, PlatformProvider);
        using var ecdsa = new ECDsaCng(key);
        return (byte[]?)VaultDeviceKeyStore.Sign(ecdsa, data);
    }, cancellationToken);

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        if (IsInTpm(name))
        {
            using var key = CngKey.Open(KeyPrefix + name, PlatformProvider);
            key.Delete();
        }

        await _software.DeleteAsync(name, cancellationToken);
    }, cancellationToken);

    private static bool IsInTpm(string name)
    {
        try
        {
            return CngKey.Exists(KeyPrefix + name, PlatformProvider);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
