using System;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Android.Security.Keystore;
using Client.Application.Ports;
using Java.Security;
using Java.Security.Spec;

namespace Client.Droid.Platform.Security;

/// <summary>
/// Device keys in the Android Keystore: made inside it, hardware-backed where the phone has secure hardware, and
/// never exportable, so a copy of the data of the app logs nobody in.
/// </summary>
internal sealed class AndroidKeystoreDeviceKeyStore : IDeviceKeyStore
{
    private const string Provider = "AndroidKeyStore";
    private const string AliasPrefix = "flowrate-device-";

    public Task<byte[]> CreateAsync(string name, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var spec = new KeyGenParameterSpec.Builder(AliasPrefix + name, KeyStorePurpose.Sign)
            .SetAlgorithmParameterSpec(new ECGenParameterSpec("secp256r1"))
            .SetDigests(KeyProperties.DigestSha256)
            .Build();
        var generator = KeyPairGenerator.GetInstance(KeyProperties.KeyAlgorithmEc, Provider)!;
        generator.Initialize(spec);
        var pair = generator.GenerateKeyPair()!;
        // X.509 SubjectPublicKeyInfo
        return pair.Public!.GetEncoded()!;
    }, cancellationToken);

    public Task<byte[]?> SignAsync(string name, byte[] data, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var store = Open();
        if (store.GetEntry(AliasPrefix + name, null) is not KeyStore.PrivateKeyEntry entry)
            return null;

        var signature = Java.Security.Signature.GetInstance("SHA256withECDSA")!;
        signature.InitSign(entry.PrivateKey);
        signature.Update(data);
        // The Keystore signs in DER; the protocol carries r‖s
        return (byte[]?)ToIeeeP1363(signature.Sign()!);
    }, cancellationToken);

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var store = Open();
        if (store.ContainsAlias(AliasPrefix + name))
            store.DeleteEntry(AliasPrefix + name);
    }, cancellationToken);

    private static KeyStore Open()
    {
        var store = KeyStore.GetInstance(Provider)!;
        store.Load(null);
        return store;
    }

    private static byte[] ToIeeeP1363(byte[] der)
    {
        var reader = new System.Formats.Asn1.AsnReader(der, System.Formats.Asn1.AsnEncodingRules.DER);
        var sequence = reader.ReadSequence();
        var r = sequence.ReadIntegerBytes().ToArray();
        var s = sequence.ReadIntegerBytes().ToArray();
        var result = new byte[64];
        Place(r, result.AsSpan(0, 32));
        Place(s, result.AsSpan(32, 32));
        return result;
    }

    // ASN.1 integers carry a leading zero for a high first bit, and drop leading zeros otherwise
    private static void Place(byte[] integer, Span<byte> target)
    {
        var value = integer.AsSpan().TrimStart((byte)0);
        if (value.Length > target.Length)
            throw new CryptographicException("The signature does not fit P-256.");
        value.CopyTo(target[(target.Length - value.Length)..]);
    }
}
