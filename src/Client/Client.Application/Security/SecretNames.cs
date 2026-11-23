using System.Security.Cryptography;
using System.Text;

namespace Client.Application.Security;

/// <summary>
/// The names secrets are kept under. Keyrings put names into file names and labels, so the parts are hashed into
/// letters and digits.
/// </summary>
public static class SecretNames
{
    public static string ServerPassword(string coordinatorId, int userId) => Of("server-password", coordinatorId, userId);

    public static string RecoveryPhrase(string coordinatorId, int userId) => Of("recovery-phrase", coordinatorId, userId);

    public static string DeviceKey(string keyName) => "device-key-" + keyName;

    /// <summary>The key the backups of an account are encrypted with, derived from its recovery phrase.</summary>
    public static string BackupKey(string coordinatorId, int userId) => Of("backup-key", coordinatorId, userId);

    /// <summary>The folder encrypted backups are kept in for other devices to pick up; not a secret, kept with them.</summary>
    public const string SyncFolder = "sync-folder";

    /// <summary>A name for the key of a new device.</summary>
    public static string NewDeviceKey() => Guid.NewGuid().ToString("N");

    private static string Of(string kind, string coordinatorId, int userId) =>
        kind + "-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{coordinatorId}\n{userId}")))[..32];
}
