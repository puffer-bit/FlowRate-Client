using Client.Core.Coordinator;


namespace Client.Application.Coordinator;

/// <summary>
/// A certificate the system could not verify and the user has not trusted for this address yet.
/// </summary>
/// <param name="KnownFingerprint">The fingerprint trusted earlier for the same address, null on the first connection.</param>
public record UntrustedCertificate(ServerCertificateModel Certificate, string? KnownFingerprint)
{
    /// <summary>The server presented a different certificate than the one trusted before.</summary>
    public bool IsChanged => KnownFingerprint != null;
}
