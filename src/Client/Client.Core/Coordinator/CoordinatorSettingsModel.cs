using Shared.Enums;
using Shared.Enums.Coordinator;

namespace Client.Core.Coordinator;

/// <summary>
/// The stored settings of the coordinator we are connected to. The server password is never sent, only whether
/// there is one.
/// </summary>
public sealed record CoordinatorSettingsModel
{
    /// <summary>Grows with every saved change; a change is accepted only against the current one.</summary>
    public long Revision { get; init; }

    public string Name { get; init; } = string.Empty;
    public string Ip { get; init; } = string.Empty;
    public int Port { get; init; }
    public bool IsPasswordProtected { get; init; }

    public int MaxOnlineUsers { get; init; }
    public int MaxUsers { get; init; }

    public bool IsMotdEnabled { get; init; }
    public string? MotdText { get; init; }

    public IReadOnlyList<string> AllowedIps { get; init; } = [];
    public IReadOnlyList<string> BannedIps { get; init; } = [];

    public int HeartbeatIntervalSeconds { get; init; }
    public int HeartbeatTimeoutSeconds { get; init; }

    public long MaxFileSizeBytes { get; init; }

    /// <summary>0 when the media storage is not limited.</summary>
    public long StorageQuotaBytes { get; init; }

    /// <summary>How many uploads one user may keep before attaching them to a message.</summary>
    public int MaxUnattachedUploadsPerUser { get; init; }

    /// <summary>How long an upload not attached to a message is kept.</summary>
    public int UnattachedUploadLifetimeHours { get; init; }

    /// <summary>How long a token for media requests stays valid.</summary>
    public int MediaTokenLifetimeMinutes { get; init; }

    public CoordinatorRateLimits RateLimits { get; init; } = new();

    public IReadOnlyList<int> SfuNodeIds { get; init; } = [];
}

/// <summary>
/// The whole new settings of the coordinator. A changed address or port restarts it, which disconnects its users.
/// </summary>
public sealed record CoordinatorSettingsUpdate
{
    public required string Name { get; init; }
    public required string Ip { get; init; }
    public int Port { get; init; }

    /// <summary>Sets a new server password. Null keeps the current one.</summary>
    public string? NewPassword { get; init; }

    /// <summary>Lets users in without a server password.</summary>
    public bool RemovePassword { get; init; }

    public int MaxOnlineUsers { get; init; }
    public int MaxUsers { get; init; }

    public bool IsMotdEnabled { get; init; }
    public string? MotdText { get; init; }

    public IReadOnlyList<string> AllowedIps { get; init; } = [];
    public IReadOnlyList<string> BannedIps { get; init; } = [];

    public int HeartbeatIntervalSeconds { get; init; }
    public int HeartbeatTimeoutSeconds { get; init; }

    public long MaxFileSizeBytes { get; init; }
    public long StorageQuotaBytes { get; init; }

    public int MaxUnattachedUploadsPerUser { get; init; }
    public int UnattachedUploadLifetimeHours { get; init; }
    public int MediaTokenLifetimeMinutes { get; init; }

    /// <summary>Not edited by the client, sent back as they were loaded.</summary>
    public CoordinatorRateLimits RateLimits { get; init; } = new();

    public IReadOnlyList<int> SfuNodeIds { get; init; } = [];
}

/// <summary>At most <see cref="Count"/> requests per <see cref="PeriodSeconds"/>.</summary>
public sealed record RequestRate(int Count, double PeriodSeconds)
{
    public static RequestRate None { get; } = new(0, 0);
}

/// <summary>
/// How many requests and connections the coordinator takes from one client. Every request falls into one group
/// and also counts against <see cref="Global"/>.
/// </summary>
public sealed record CoordinatorRateLimits
{
    /// <summary>Every request of one connection.</summary>
    public RequestRate Global { get; init; } = RequestRate.None;

    /// <summary>Logins from one IP address, over all its connections.</summary>
    public RequestRate AuthenticationPerIp { get; init; } = RequestRate.None;

    public RequestRate SessionManagement { get; init; } = RequestRate.None;
    public RequestRate SessionLayout { get; init; } = RequestRate.None;
    public RequestRate SessionMedia { get; init; } = RequestRate.None;
    public RequestRate MicrophoneMute { get; init; } = RequestRate.None;
    public RequestRate InboundResume { get; init; } = RequestRate.None;
    public RequestRate Moderation { get; init; } = RequestRate.None;
    public RequestRate ChatWrite { get; init; } = RequestRate.None;
    public RequestRate ChatRead { get; init; } = RequestRate.None;
    public RequestRate PushRegistration { get; init; } = RequestRate.None;
    public RequestRate MediaToken { get; init; } = RequestRate.None;

    /// <summary>Every request of no other group.</summary>
    public RequestRate Query { get; init; } = RequestRate.None;

    public RequestRate Heartbeat { get; init; } = RequestRate.None;

    /// <summary>A connection that has this many requests refused within the window is closed.</summary>
    public int MaxRejectionsPerWindow { get; init; }
    public int RejectionWindowSeconds { get; init; }

    /// <summary>How many connections one IP address may keep open.</summary>
    public int MaxConnectionsPerIp { get; init; }
}

/// <summary>A node of the SFU pool of the server that the coordinator can be given.</summary>
/// <param name="Connection">How the server's connection to the node was doing when it answered; null for a disabled node.</param>
public sealed record SfuNodeModel(int Id, string Name, string Host, int Port, bool IsEnabled,
    SfuNodeConnection? Connection = null);

/// <param name="RoundTrip">The server's last answered ping to the node; null while the node does not answer.</param>
/// <param name="Error">Why the node is not connected, if the server knows.</param>
public sealed record SfuNodeConnection(SfuNodeConnectionState State, TimeSpan? RoundTrip, string? Error);

/// <summary>The settings together with the SFU nodes they may name.</summary>
public sealed record CoordinatorSettingsSnapshot(CoordinatorSettingsModel Settings, IReadOnlyList<SfuNodeModel> SfuNodes);

/// <param name="Settings">The settings as they are now: the saved ones, or on a conflict the newer ones.</param>
/// <param name="Errors">Why values were refused, by field ("Heartbeat.TimeoutSeconds"; "" for the whole change).</param>
public sealed record CoordinatorSettingsUpdateOutcome(
    CoordinatorSettingsResult Result,
    CoordinatorSettingsModel? Settings,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static CoordinatorSettingsUpdateOutcome Of(CoordinatorSettingsResult result) =>
        new(result, null, new Dictionary<string, string[]>());
}
