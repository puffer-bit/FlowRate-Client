using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using BigInteger = Org.BouncyCastle.Math.BigInteger;

namespace Client.Browser.Platform;

/// <summary>
/// P-256 ECDsa in managed code: .NET in the browser has no ECDsa of its own (Arg_PlatformNotSupported), and the
/// login signs with one. The base class does PKCS#8/SPKI and the hashing, this does the key and the signature.
/// </summary>
internal sealed class BouncyCastleECDsa : ECDsa
{
    private const string P256Oid = "1.2.840.10045.3.1.7";
    private const int FieldBytes = 32;
    private static readonly ECDomainParameters Domain = new(ECNamedCurveTable.GetByName("P-256"));

    private ECPrivateKeyParameters? _private;
    private ECPublicKeyParameters? _public;

    public BouncyCastleECDsa()
    {
        KeySizeValue = 256;
        LegalKeySizesValue = [new KeySizes(256, 256, 0)];
    }

    public override void GenerateKey(ECCurve curve)
    {
        CheckCurve(curve);
        var generator = new ECKeyPairGenerator();
        generator.Init(new ECKeyGenerationParameters(Domain, new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        _private = (ECPrivateKeyParameters)pair.Private;
        _public = (ECPublicKeyParameters)pair.Public;
    }

    public override void ImportParameters(ECParameters parameters)
    {
        parameters.Validate();
        CheckCurve(parameters.Curve);
        var d = parameters.D is { } scalar ? new BigInteger(1, scalar) : null;
        var q = parameters.Q.X is { } x && parameters.Q.Y is { } y
            ? Domain.Curve.ValidatePoint(new BigInteger(1, x), new BigInteger(1, y))
            : Domain.G.Multiply(d ?? throw new CryptographicException("The key has neither a public nor a private part.")).Normalize();
        _private = d is null ? null : new ECPrivateKeyParameters(d, Domain);
        _public = new ECPublicKeyParameters(q, Domain);
    }

    public override ECParameters ExportParameters(bool includePrivateParameters)
    {
        var publicKey = _public ?? throw new CryptographicException("There is no key.");
        if (includePrivateParameters && _private is null)
            throw new CryptographicException("The key has no private part.");

        var q = publicKey.Q.Normalize();
        return new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = q.AffineXCoord.GetEncoded(), Y = q.AffineYCoord.GetEncoded() },
            D = includePrivateParameters ? _private!.D.ToByteArrayUnsigned().PadLeft(FieldBytes) : null
        };
    }

    public override ECParameters ExportExplicitParameters(bool includePrivateParameters) =>
        throw new PlatformNotSupportedException("Only named P-256 keys are supported.");

    public override byte[] SignHash(byte[] hash)
    {
        var signer = new ECDsaSigner(new HMacDsaKCalculator(new Sha256Digest()));
        signer.Init(true, _private ?? throw new CryptographicException("The key has no private part."));
        var rs = signer.GenerateSignature(hash);
        return [.. rs[0].ToByteArrayUnsigned().PadLeft(FieldBytes), .. rs[1].ToByteArrayUnsigned().PadLeft(FieldBytes)];
    }

    public override bool VerifyHash(byte[] hash, byte[] signature)
    {
        if (_public is null || signature.Length != 2 * FieldBytes)
            return false;
        var signer = new ECDsaSigner();
        signer.Init(false, _public);
        return signer.VerifySignature(hash, new BigInteger(1, signature, 0, FieldBytes),
            new BigInteger(1, signature, FieldBytes, FieldBytes));
    }

    private static void CheckCurve(ECCurve curve)
    {
        if (!curve.IsNamed || (curve.Oid.Value != P256Oid && curve.Oid.FriendlyName is not ("nistP256" or "ECDSA_P256")))
            throw new PlatformNotSupportedException("Only P-256 keys are supported.");
    }
}

file static class ByteArrayExtensions
{
    public static byte[] PadLeft(this byte[] value, int length)
    {
        if (value.Length >= length)
            return value;
        var padded = new byte[length];
        value.CopyTo(padded, length - value.Length);
        return padded;
    }
}
