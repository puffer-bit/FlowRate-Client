namespace Client.Core.Coordinator;

/// <summary>
/// Certificate a coordinator presented for the TLS connection.
/// </summary>
/// <param name="Fingerprint">SHA-256 of the certificate as uppercase hex, the same form the server logs.</param>
/// <param name="CoordinatorId">
/// The coordinator that answered behind this certificate. Bound after the first login, because the
/// coordinator tells its id only to an authenticated client; null until then.
/// </param>
public record ServerCertificateModel(
    string Host,
    int Port,
    string Fingerprint,
    string Subject,
    DateTime NotAfter,
    string? CoordinatorId = null);
