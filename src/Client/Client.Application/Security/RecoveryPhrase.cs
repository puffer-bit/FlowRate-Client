using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Client.Application.Security;

/// <summary>
/// The recovery phrase of an account: 24 English words of BIP-39, 256 random bits with an 8-bit checksum. It never
/// leaves the device; the server keeps only the public keys it derives (<see cref="RecoveryKeySet"/>).
/// </summary>
public static class RecoveryPhrase
{
    public const int WordCount = 24;
    private const int EntropyBytes = 32;

    private static readonly Lazy<string[]> Words = new(LoadWords);
    private static readonly Lazy<Dictionary<string, int>> Indexes = new(() =>
    {
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < Words.Value.Length; i++)
        {
            indexes[Words.Value[i]] = i;
            // The first four letters tell every word apart, as BIP-39 promises
            indexes.TryAdd(Words.Value[i][..Math.Min(4, Words.Value[i].Length)], i);
        }
        return indexes;
    });

    /// <summary>A new phrase from 256 random bits.</summary>
    public static string Generate()
    {
        var entropy = RandomNumberGenerator.GetBytes(EntropyBytes);
        try
        {
            return FromEntropy(entropy);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    /// <summary>
    /// The 256 bits of a phrase as typed: case, spaces, commas and numbering do not matter, nor whether whole words or
    /// their first four letters are given. Null when a word is unknown, the count is wrong or the checksum fails.
    /// </summary>
    public static byte[]? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var parts = text.ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n', ',', ';', '.', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.All(char.IsDigit))
            .ToArray();
        if (parts.Length != WordCount)
            return null;

        var bits = BigInteger.Zero;
        foreach (var part in parts)
        {
            if (!Indexes.Value.TryGetValue(part, out var index))
                return null;
            bits = (bits << 11) | index;
        }

        // 264 bits: the entropy, then the first byte of its SHA-256
        var checksum = (byte)(bits & 0xFF);
        var entropy = ToFixedBytes(bits >> 8, EntropyBytes);
        return SHA256.HashData(entropy)[0] == checksum ? entropy : null;
    }

    /// <summary>The phrase as words, normalized: the full words, lower case, separated by single spaces.</summary>
    public static string? Normalize(string? text) => TryParse(text) is { } entropy ? FromEntropy(entropy) : null;

    public static string FromEntropy(byte[] entropy)
    {
        if (entropy.Length != EntropyBytes)
            throw new ArgumentException("A phrase holds 32 bytes.", nameof(entropy));

        var bits = (new BigInteger(entropy, isUnsigned: true, isBigEndian: true) << 8) | SHA256.HashData(entropy)[0];
        var words = new string[WordCount];
        for (var i = WordCount - 1; i >= 0; i--)
        {
            words[i] = Words.Value[(int)(bits & 0x7FF)];
            bits >>= 11;
        }
        return string.Join(' ', words);
    }

    /// <summary>The words of a phrase, numbered from 1, for showing them in a grid.</summary>
    public static IReadOnlyList<string> Split(string phrase) =>
        phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string[] LoadWords()
    {
        using var stream = typeof(RecoveryPhrase).Assembly.GetManifestResourceStream("Client.Application.Bip39English.txt")
                           ?? throw new InvalidOperationException("The BIP-39 word list is not embedded.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var words = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length != 2048)
            throw new InvalidOperationException("The BIP-39 word list has to hold 2048 words.");
        return words;
    }

    internal static byte[] ToFixedBytes(BigInteger value, int length)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length == length)
            return bytes;
        var padded = new byte[length];
        bytes.CopyTo(padded, length - bytes.Length);
        return padded;
    }
}

/// <summary>
/// The keys a recovery phrase derives with HKDF-SHA256: the recovery key a login with the phrase signs with, the
/// identity key of the account for end-to-end encryption, and the key backups of the account are encrypted with.
/// </summary>
public sealed class RecoveryKeySet : IDisposable
{
    private static readonly byte[] Salt = "FlowRate recovery v1"u8.ToArray();

    private RecoveryKeySet(ECDsa recoveryKey, ECDsa accountKey, byte[] backupKey)
    {
        RecoveryKey = recoveryKey;
        AccountKey = accountKey;
        BackupKey = backupKey;
    }

    /// <summary>Signs logins with the phrase; the server knows its public part.</summary>
    public ECDsa RecoveryKey { get; }

    /// <summary>The identity key of the account for end-to-end encryption; only its public part is used yet.</summary>
    public ECDsa AccountKey { get; }

    /// <summary>A 256-bit key for the encrypted backups of the account.</summary>
    public byte[] BackupKey { get; }

    public byte[] RecoveryPublicKey => RecoveryKey.ExportSubjectPublicKeyInfo();
    public byte[] AccountPublicKey => AccountKey.ExportSubjectPublicKeyInfo();

    public static RecoveryKeySet? FromPhrase(string phrase) =>
        RecoveryPhrase.TryParse(phrase) is { } entropy ? FromEntropy(entropy) : null;

    public static RecoveryKeySet FromEntropy(byte[] entropy)
    {
        try
        {
            return new RecoveryKeySet(P256.FromSeed(Derive(entropy, "login", 48)),
                P256.FromSeed(Derive(entropy, "identity", 48)), Derive(entropy, "backup", 32));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    private static byte[] Derive(byte[] entropy, string label, int length) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, entropy, length, Salt, Encoding.UTF8.GetBytes(label));

    public void Dispose()
    {
        RecoveryKey.Dispose();
        AccountKey.Dispose();
        CryptographicOperations.ZeroMemory(BackupKey);
    }
}

/// <summary>
/// Makes a P-256 key from derived bytes. The public point is computed here, so the key imports the same on every
/// platform, including those that do not compute it from the private scalar themselves.
/// </summary>
internal static class P256
{
    private static readonly BigInteger P = Parse("ffffffff00000001000000000000000000000000ffffffffffffffffffffffff");
    private static readonly BigInteger N = Parse("ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551");
    private static readonly BigInteger A = P - 3;
    private static readonly (BigInteger X, BigInteger Y) G = (
        Parse("6b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c296"),
        Parse("4fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5"));

    /// <summary>The key whose scalar is <paramref name="seed"/> reduced into 1…n−1 (48 bytes keep the bias negligible).</summary>
    public static ECDsa FromSeed(byte[] seed)
    {
        try
        {
            var d = new BigInteger(seed, isUnsigned: true, isBigEndian: true) % (N - 1) + 1;
            var (x, y) = Multiply(d, G);
            return ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = RecoveryPhrase.ToFixedBytes(d, 32),
                Q = new ECPoint { X = RecoveryPhrase.ToFixedBytes(x, 32), Y = RecoveryPhrase.ToFixedBytes(y, 32) }
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(seed);
        }
    }

    private static (BigInteger X, BigInteger Y) Multiply(BigInteger k, (BigInteger X, BigInteger Y) point)
    {
        (BigInteger X, BigInteger Y)? result = null;
        var addend = point;
        while (k > 0)
        {
            if (!k.IsEven)
                result = result is { } r ? Add(r, addend) : addend;
            addend = Add(addend, addend);
            k >>= 1;
        }
        return result ?? throw new ArgumentOutOfRangeException(nameof(k));
    }

    // Affine addition; the scalars here never reach the point at infinity on the way
    private static (BigInteger X, BigInteger Y) Add((BigInteger X, BigInteger Y) p1, (BigInteger X, BigInteger Y) p2)
    {
        BigInteger slope;
        if (p1.X == p2.X && p1.Y == p2.Y)
            slope = Mod((3 * p1.X * p1.X + A) * Inverse(2 * p1.Y));
        else
            slope = Mod((p2.Y - p1.Y) * Inverse(p2.X - p1.X));

        var x = Mod(slope * slope - p1.X - p2.X);
        var y = Mod(slope * (p1.X - x) - p1.Y);
        return (x, y);
    }

    private static BigInteger Mod(BigInteger value)
    {
        var result = value % P;
        return result.Sign < 0 ? result + P : result;
    }

    private static BigInteger Inverse(BigInteger value)
    {
        // Extended Euclid
        BigInteger a = Mod(value), m = P, x0 = 0, x1 = 1;
        while (a > 1)
        {
            var q = a / m;
            (a, m) = (m, a % m);
            (x0, x1) = (x1 - q * x0, x0);
        }
        return Mod(x1);
    }

    private static BigInteger Parse(string hex) => BigInteger.Parse("0" + hex, System.Globalization.NumberStyles.HexNumber);
}
