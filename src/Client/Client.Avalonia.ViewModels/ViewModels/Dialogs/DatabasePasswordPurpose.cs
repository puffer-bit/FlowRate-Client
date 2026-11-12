namespace Client.ViewModels.Dialogs;

public enum DatabasePasswordPurpose
{
    /// <summary>At start: the encrypted database waits for its password.</summary>
    Unlock,

    /// <summary>The database is to be encrypted with a new password.</summary>
    Create,

    /// <summary>The encrypted database gets another password.</summary>
    Change
}
