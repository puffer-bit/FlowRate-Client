using Client.Core.Coordinator;


namespace Client.Application.Ports;

/// <param name="RejectedCertificate">Set when the connection failed because the server certificate is not trusted.</param>
/// <param name="ProtocolMismatch">Set when the server does not serve the protocol version of this client.</param>
/// <param name="IsCertificateVerified">
/// The system verified the certificate of the server; false when it was accepted only because the user trusted it.
/// </param>
public record TransportConnectResult(
    bool IsSuccess,
    string? ErrorMessage = null,
    ServerCertificateModel? RejectedCertificate = null,
    ProtocolMismatch? ProtocolMismatch = null,
    bool IsCertificateVerified = false);
