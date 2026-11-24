using System;
using System.Security.Cryptography;
using Client.Application.Security;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace Client.Browser.Platform;

/// <summary>AES-GCM of BouncyCastle for the browser, whose .NET has no <see cref="AesGcm"/>; the same bytes as the system one.</summary>
internal sealed class BouncyCastleAesGcm : IAesGcmCipher
{
    private readonly KeyParameter _key;
    private readonly int _tagLength;

    public BouncyCastleAesGcm(byte[] key, int tagLength)
    {
        if (key.Length is not (16 or 24 or 32))
            throw new CryptographicException("The AES key must be 16, 24 or 32 bytes long.");
        if (tagLength is < 12 or > 16)
            throw new ArgumentOutOfRangeException(nameof(tagLength));
        _key = new KeyParameter(key);
        _tagLength = tagLength;
    }

    public void Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, Span<byte> tag,
        ReadOnlySpan<byte> associatedData = default)
    {
        if (ciphertext.Length != plaintext.Length || tag.Length != _tagLength)
            throw new ArgumentException("The ciphertext must be as long as the plaintext and the tag as long as set.");

        var gcm = Create(true, nonce, associatedData);
        var output = new byte[gcm.GetOutputSize(plaintext.Length)];
        var written = gcm.ProcessBytes(plaintext, output);
        written += gcm.DoFinal(output.AsSpan(written));
        output.AsSpan(0, plaintext.Length).CopyTo(ciphertext);
        output.AsSpan(plaintext.Length, _tagLength).CopyTo(tag);
        CryptographicOperations.ZeroMemory(output);
    }

    public void Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag, Span<byte> plaintext,
        ReadOnlySpan<byte> associatedData = default)
    {
        if (plaintext.Length != ciphertext.Length || tag.Length != _tagLength)
            throw new ArgumentException("The plaintext must be as long as the ciphertext and the tag as long as set.");

        // BouncyCastle takes the tag after the ciphertext
        var input = new byte[ciphertext.Length + tag.Length];
        ciphertext.CopyTo(input);
        tag.CopyTo(input.AsSpan(ciphertext.Length));

        var gcm = Create(false, nonce, associatedData);
        var output = new byte[gcm.GetOutputSize(input.Length)];
        try
        {
            var written = gcm.ProcessBytes(input, output);
            gcm.DoFinal(output.AsSpan(written));
            output.AsSpan(0, plaintext.Length).CopyTo(plaintext);
        }
        catch (InvalidCipherTextException exception)
        {
            plaintext.Clear();
            throw new AuthenticationTagMismatchException(exception.Message);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(output);
        }
    }

    private GcmBlockCipher Create(bool encrypt, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> associatedData)
    {
        if (nonce.Length != 12)
            throw new ArgumentException("The AES-GCM nonce must be 12 bytes long.", nameof(nonce));
        var gcm = new GcmBlockCipher(new AesEngine());
        gcm.Init(encrypt, new AeadParameters(_key, _tagLength * 8, nonce.ToArray(), associatedData.ToArray()));
        return gcm;
    }

    public void Dispose()
    {
    }
}
