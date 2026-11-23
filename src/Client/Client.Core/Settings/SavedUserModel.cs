namespace Client.Core.Settings;

/// <summary>
/// An account remembered on this device. Secrets are not kept in the database: the server password, the device key
/// and an unsaved recovery phrase live in the system keyring (see <c>ISecretVault</c>).
/// </summary>
public class SavedUserModel
{
    public int Id { get; init; }

    /// <summary>Coordinator label used to distinguish accounts at the same endpoint.</summary>
    public string? CoordinatorLabel { get; set; }
    public string SelectionLabel => CoordinatorLabel == null ? Username : $"{Username} ({CoordinatorLabel})";
    public string Username { get; init; } = string.Empty;
    public string CoordinatorId { get; init; } = string.Empty;

    /// <summary>The password of the server, kept when the user asked to remember it.</summary>
    public string? Password { get; init; }

    /// <summary>The device this client logs in as; null until the account has one here, or after it ended.</summary>
    public Guid? DeviceId { get; init; }

    /// <summary>The name of the key of the device in the key store.</summary>
    public string? DeviceKey { get; init; }

    /// <summary>
    /// The recovery phrase while it is kept on this device: until the user confirms saving it, or when they chose to
    /// keep it.
    /// </summary>
    public string? RecoveryPhrase { get; init; }

    /// <summary>
    /// The key backups of the account are encrypted with (base64), known when the recovery phrase was: kept in the
    /// keyring, never in the database.
    /// </summary>
    public string? BackupKey { get; init; }

    /// <summary>The user confirmed saving the recovery phrase.</summary>
    public bool IsRecoveryPhraseSaved { get; init; }

    /// <summary>
    /// The identity of the earlier protocol 3; logs in once to move the account over to a device, empty afterwards.
    /// </summary>
    public string UserIdentity { get; init; } = string.Empty;

    /// <summary>The session token of the earlier protocol 3, skipping the second stage of that one login.</summary>
    public string? SessionToken { get; init; }

    /// <summary>The account password the earlier client remembered, for that one login; never kept again.</summary>
    public string? AccountPassword { get; init; }

    /// <summary>The account can log in without asking: it has a device here, or an identity to move over.</summary>
    public bool CanLogIn => DeviceId != null && DeviceKey != null || !string.IsNullOrEmpty(UserIdentity);

    /// <summary>
    /// The avatar the user had when last seen; not saved with the account itself.
    /// </summary>
    public string? AvatarUrl { get; init; }

    /// <summary>
    /// Set only on the entries of an account list that are not accounts (a new one, one to log in to): the key of the
    /// icon shown in place of the avatar.
    /// </summary>
    public string? EntryIconKey { get; init; }

    /// <summary>The same account once its device here was ended: it has to log in again.</summary>
    public SavedUserModel WithoutDevice() => new()
    {
        Id = Id,
        CoordinatorLabel = CoordinatorLabel,
        Username = Username,
        CoordinatorId = CoordinatorId,
        Password = Password,
        RecoveryPhrase = RecoveryPhrase,
        BackupKey = BackupKey,
        IsRecoveryPhraseSaved = IsRecoveryPhraseSaved,
        AvatarUrl = AvatarUrl,
        EntryIconKey = EntryIconKey
    };
}
