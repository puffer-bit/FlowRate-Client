using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Client.Application.Sync;

/// <summary>
/// What an encrypted backup of an account holds: the server, the private notes about other users and the muted chats.
/// Never the keys of devices nor passwords: a backup restores what the user set up here, and the recovery phrase it is
/// encrypted with logs in on its own.
/// </summary>
public sealed record AccountBackup
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public required string CoordinatorId { get; init; }
    public required int UserId { get; init; }
    public required string Username { get; init; }
    public required string Address { get; init; }
    public required int Port { get; init; }
    public string? ServerName { get; init; }
    public Dictionary<int, string> Notes { get; init; } = [];
    public List<int> MutedChats { get; init; } = [];
    public DateTimeOffset ExportedAt { get; init; }

    /// <summary>The device that wrote it, so a sync folder tells its own file from the others.</summary>
    public Guid? DeviceId { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(AccountBackup))]
internal sealed partial class AccountBackupJsonContext : JsonSerializerContext;

/// <summary>
/// The file format: "FRB1", a random salt, the AES-256-GCM nonce and tag, then the encrypted JSON. The key of each file
/// is derived from the backup key of the account (HKDF-SHA256 with the salt), so no two files share a key.
/// </summary>
public static class AccountBackupFile
{
    public const string Extension = "frb";

    private static readonly byte[] Magic = "FRB1"u8.ToArray();
    private static readonly byte[] Info = "FlowRate backup v1"u8.ToArray();
    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;

    public static byte[] Seal(AccountBackup backup, byte[] backupKey)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(backup, AccountBackupJsonContext.Default.AccountBackup);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, backupKey, 32, salt, Info);
        try
        {
            var file = new byte[Magic.Length + SaltLength + NonceLength + TagLength + plain.Length];
            var span = file.AsSpan();
            Magic.CopyTo(span);
            salt.CopyTo(span[Magic.Length..]);
            nonce.CopyTo(span[(Magic.Length + SaltLength)..]);
            var tag = span.Slice(Magic.Length + SaltLength + NonceLength, TagLength);
            var cipher = span[(Magic.Length + SaltLength + NonceLength + TagLength)..];
            using var aes = new AesGcm(key, TagLength);
            // The header is authenticated too
            aes.Encrypt(nonce, plain, cipher, tag, span[..(Magic.Length + SaltLength)]);
            return file;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>The backup, or null when the file is not one or the key is not the one it was sealed with.</summary>
    public static AccountBackup? TryOpen(byte[] file, byte[] backupKey)
    {
        var header = Magic.Length + SaltLength + NonceLength + TagLength;
        if (file.Length < header || !file.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            return null;

        var span = file.AsSpan();
        var salt = span.Slice(Magic.Length, SaltLength).ToArray();
        var nonce = span.Slice(Magic.Length + SaltLength, NonceLength);
        var tag = span.Slice(Magic.Length + SaltLength + NonceLength, TagLength);
        var cipher = span[header..];
        var plain = new byte[cipher.Length];
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, backupKey, 32, salt, Info);
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, cipher, tag, plain, span[..(Magic.Length + SaltLength)]);
            var backup = JsonSerializer.Deserialize(plain, AccountBackupJsonContext.Default.AccountBackup);
            return backup is { Version: <= AccountBackup.CurrentVersion } ? backup : null;
        }
        catch (Exception exception) when (exception is AuthenticationTagMismatchException or CryptographicException
                                              or JsonException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>The name of the file of an account in a sync folder: the same on every device of the account.</summary>
    public static string SyncName(string coordinatorId, int userId, Guid deviceId) =>
        $"flowrate-{AccountTag(coordinatorId, userId)}-{deviceId:N}.{Extension}";

    public static string AccountTag(string coordinatorId, int userId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{coordinatorId}\n{userId}")))[..16];
}
