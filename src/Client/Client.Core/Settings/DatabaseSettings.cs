namespace Client.Core.Settings;

public class DatabaseSettings
{
    public bool IsDatabaseEncryptionEnabled { get; set; }
    public bool IsDatabasePasswordSaveEnabled { get; set; }
    public bool IsDatabaseLockEnabled { get; set; }
}