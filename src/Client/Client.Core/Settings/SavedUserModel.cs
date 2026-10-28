namespace Client.Core.Settings;

public class SavedUserModel
{
    public int Id { get; init; }
    public string UserIdentity { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    
    /// <summary>The password of the server.</summary>
    public string? Password { get; init; }

    /// <summary>The password of the user's account, kept when the user asked to remember it.</summary>
    public string? AccountPassword { get; init; }

    /// <summary>The token of the device's session: logs in without the second stage until the session ends.</summary>
    public string? SessionToken { get; init; }
    public string CoordinatorId { get; init; } = string.Empty;

    /// <summary>
    /// The avatar the user had when last seen; not saved with the account itself.
    /// </summary>
    public string? AvatarUrl { get; init; }
}