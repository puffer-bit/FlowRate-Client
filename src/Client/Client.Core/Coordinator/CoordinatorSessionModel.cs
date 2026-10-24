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
        
    public DateTime CreateTime { get; init; }
    public DateTime FirstLaunchTime { get; init; }
    public DateTime CurrentLaunchTime { get; init; }
    public int MaxOnlineUsers { get; init; }
    public int MaxUsers { get; init; }
    
    public bool IsMOTDEnabled { get; init; }

    /// <summary>The revision of the stored settings these values come from.</summary>
    public long SettingsRevision { get; init; }
}