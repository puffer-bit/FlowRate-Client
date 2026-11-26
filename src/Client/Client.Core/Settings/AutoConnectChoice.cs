namespace Client.Core.Settings;

/// <summary>
/// The server and account the application connects to when it starts. A part left null follows the last successful
/// connection: no server is the last server with its last account, no account is the last account of the server.
/// </summary>
public sealed record AutoConnectChoice(string? CoordinatorId, int? UserId)
{
    /// <summary>The last successful connection, whatever it was.</summary>
    public static AutoConnectChoice Last { get; } = new(null, null);
}
