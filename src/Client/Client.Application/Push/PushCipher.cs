using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Client.Core.Push;

namespace Client.Application.Push;

/// <summary>
/// Opens the content of a push notification: base64 of a 12-byte nonce, the AES-256-GCM
/// ciphertext of the JSON payload and a 16-byte tag.
/// </summary>
public static class PushCipher
{
    private const int NonceLength = 12;
    private const int TagLength = 16;

    /// <returns>Null when the content was not made with <paramref name="key"/> or is malformed.</returns>
    public static PushPayload? Decrypt(byte[] key, string sealedText)
    {
        try
        {
            var sealedMessage = Convert.FromBase64String(sealedText);
            if (sealedMessage.Length < NonceLength + TagLength)
                return null;

            var plaintext = new byte[sealedMessage.Length - NonceLength - TagLength];

            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(sealedMessage.AsSpan(0, NonceLength), sealedMessage.AsSpan(NonceLength, plaintext.Length),
                sealedMessage.AsSpan(NonceLength + plaintext.Length), plaintext);

            return JsonSerializer.Deserialize(plaintext, PushJsonContext.Default.PushPayload);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or JsonException
                                              or ArgumentException)
        {
            return null;
        }
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PushPayload))]
internal sealed partial class PushJsonContext : JsonSerializerContext;
