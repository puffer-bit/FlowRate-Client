using System.Security.Cryptography;

namespace Client.Application.Security;

/// <summary>AES-GCM with the calls of <see cref="AesGcm"/>; a wrong key or tag throws a <see cref="CryptographicException"/>.</summary>
public interface IAesGcmCipher : IDisposable
{
    void Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, Span<byte> tag,
        ReadOnlySpan<byte> associatedData = default);

    void Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag, Span<byte> plaintext,
        ReadOnlySpan<byte> associatedData = default);
}

/// <summary>
/// Where the client's AES-GCM comes from. The system <see cref="AesGcm"/> by default; a platform that has none of its
/// own (the browser) sets <see cref="Factory"/> to its implementation before anything encrypts.
/// </summary>
public static class AesGcmCiphers
{
    /// <summary>Makes a cipher of the key with tags of the given length in bytes.</summary>
    public static Func<byte[], int, IAesGcmCipher> Factory { get; set; } = (key, tagLength) => new SystemAesGcm(key, tagLength);

    public static IAesGcmCipher Create(byte[] key, int tagLength) => Factory(key, tagLength);

    private sealed class SystemAesGcm(byte[] key, int tagLength) : IAesGcmCipher
    {
        private readonly AesGcm _aes = new(key, tagLength);

        public void Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, Span<byte> tag,
            ReadOnlySpan<byte> associatedData = default) =>
            _aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        public void Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag,
            Span<byte> plaintext, ReadOnlySpan<byte> associatedData = default) =>
            _aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);

        public void Dispose() => _aes.Dispose();
    }
}
