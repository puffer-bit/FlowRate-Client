using Shared.Protocol;

namespace Client.Application.Ports;

/// <summary>The protocol versions a server serves, when they do not include the one of this client.</summary>
/// <param name="ServerVersion">The newest version the server speaks; 0 when it did not say.</param>
/// <param name="ServerMinVersion">The oldest client version it serves; 0 when it did not say.</param>
public record ProtocolMismatch(int ServerVersion, int ServerMinVersion)
{
    /// <summary>
    /// The server is newer than this client. A refusal that names no versions counts as such: only a
    /// server that knows the check refuses, and that one is at least as new as the check.
    /// </summary>
    public bool IsClientOutdated => ServerVersion == 0 || ProtocolVersion.Current < ServerMinVersion;
}
