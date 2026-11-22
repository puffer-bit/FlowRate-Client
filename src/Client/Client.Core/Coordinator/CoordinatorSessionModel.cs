namespace Client.Core.Coordinator;

public class CoordinatorSessionModel
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; init; }

    /// <summary>Kept up to date while connected, as users come and go.</summary>
    public int ConnectedUsersCount { get; set; }
    
    public string ServerVersion { get; init; } = string.Empty;

    /// <summary>The OS and architecture the server runs on, such as "Linux x64".</summary>
    public string ServerPlatform { get; init; } = string.Empty;
        
    public DateTimeOffset CreateTime { get; init; }
    public DateTimeOffset FirstLaunchTime { get; init; }
    public DateTimeOffset CurrentLaunchTime { get; init; }
    public int MaxOnlineUsers { get; init; }
    public int MaxUsers { get; init; }
    
    public bool IsMOTDEnabled { get; init; }

    /// <summary>The revision of the stored settings these values come from.</summary>
    public long SettingsRevision { get; init; }

    /// <summary>How images are compressed before they are sent, and whether they may go uncompressed.</summary>
    public Chat.ImageCompressionPolicy ImagePolicy { get; init; } = Chat.ImageCompressionPolicy.Default;

    /// <summary>How videos are compressed before they are sent, and whether they may go uncompressed.</summary>
    public Chat.VideoCompressionPolicy VideoPolicy { get; init; } = Chat.VideoCompressionPolicy.Default;
}