using Client.Core.Coordinator;


namespace Client.Application.Ports.Repositories;

/// <summary>
/// Certificates the user trusted, one per server address and port.
/// </summary>
public interface ITrustedCertificatesRepository
{
    Task<ServerCertificateModel?> GetAsync(string host, int port);

    Task<IReadOnlyList<ServerCertificateModel>> GetAllAsync();

    /// <summary>
    /// Trusts the certificate for its address, replacing the one trusted before together with its
    /// coordinator binding.
    /// </summary>
    Task SaveAsync(ServerCertificateModel certificate);

    /// <summary>Binds the coordinator that answered behind the certificate trusted for this address.</summary>
    Task BindCoordinatorAsync(string host, int port, string coordinatorId);

    Task RemoveAsync(string host, int port);
}
