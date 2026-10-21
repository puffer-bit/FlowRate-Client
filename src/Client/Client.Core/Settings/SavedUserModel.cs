namespace Client.Core.Settings;

public class SavedUserModel
{
    public int Id { get; init; }
    public string UserIdentity { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    
    public string? Password { get; init; }
    public string CoordinatorId { get; init; } = string.Empty;

    /// <summary>
    /// The avatar the user had when last seen; not saved with the account itself.
    /// </summary>
    public string? AvatarUrl { get; init; }
}