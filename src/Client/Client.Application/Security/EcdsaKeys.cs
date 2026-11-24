using System.Security.Cryptography;

namespace Client.Application.Security;

/// <summary>
/// Where the client's P-256 keys come from. The system ECDsa by default; a platform that has none of its own (the
/// browser) sets <see cref="Factory"/> to its implementation before anything signs.
/// </summary>
public static class EcdsaKeys
{
    /// <summary>Makes an empty ECDsa: the keys below are generated in it or imported into it.</summary>
    public static Func<ECDsa> Factory { get; set; } = () => ECDsa.Create();

    public static ECDsa Generate()
    {
        var key = Factory();
        key.GenerateKey(ECCurve.NamedCurves.nistP256);
        return key;
    }

    public static ECDsa Import(ECParameters parameters)
    {
        var key = Factory();
        key.ImportParameters(parameters);
        return key;
    }

    public static ECDsa ImportPkcs8(ReadOnlySpan<byte> privateKey)
    {
        var key = Factory();
        key.ImportPkcs8PrivateKey(privateKey, out _);
        return key;
    }
}
