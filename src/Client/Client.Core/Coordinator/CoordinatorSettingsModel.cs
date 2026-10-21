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

    public IceServerSettings Stun { get; init; } = IceServerSettings.Disabled;
    public IceServerSettings Turn { get; init; } = IceServerSettings.Disabled;

    public long MaxFileSizeBytes { get; init; }

    /// <summary>0 when the media storage is not limited.</summary>
    public long StorageQuotaBytes { get; init; }

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

    public IceServerSettings Stun { get; init; } = IceServerSettings.Disabled;
    public IceServerSettings Turn { get; init; } = IceServerSettings.Disabled;

    public long MaxFileSizeBytes { get; init; }
    public long StorageQuotaBytes { get; init; }

    public IReadOnlyList<int> SfuNodeIds { get; init; } = [];
}

/// <summary>A STUN or a TURN server; STUN has no credentials.</summary>
public sealed record IceServerSettings(bool IsEnabled, string? Address, int? Port, string? Username = null,
    string? Password = null)
{
    public static IceServerSettings Disabled { get; } = new(false, null, null);
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
