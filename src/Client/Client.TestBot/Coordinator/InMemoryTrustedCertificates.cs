using System.Collections.Concurrent;
using Client.Core.Coordinator;
using Client.Application.Ports.Repositories;

namespace Client.TestBot;

/// <summary>
/// Trusted certificates for the lifetime of the process. The bots run against a test server, so
/// they trust whatever it presents on the first connection instead of asking.
/// </summary>
public sealed class InMemoryTrustedCertificates : ITrustedCertificatesRepository
{
    private readonly ConcurrentDictionary<(string Host, int Port), ServerCertificateModel> _certificates = new();

    public Task<ServerCertificateModel?> GetAsync(string host, int port) =>
        Task.FromResult(_certificates.GetValueOrDefault(Key(host, port)));

    public Task<IReadOnlyList<ServerCertificateModel>> GetAllAsync() =>
        Task.FromResult<IReadOnlyList<ServerCertificateModel>>(_certificates.Values.ToList());

    public Task SaveAsync(ServerCertificateModel certificate)
    {
        _certificates[Key(certificate.Host, certificate.Port)] = certificate;
        return Task.CompletedTask;
    }

    public Task BindCoordinatorAsync(string host, int port, string coordinatorId)
    {
        if (_certificates.TryGetValue(Key(host, port), out var certificate))
            _certificates[Key(host, port)] = certificate with { CoordinatorId = coordinatorId };
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string host, int port)
    {
        _certificates.TryRemove(Key(host, port), out _);
        return Task.CompletedTask;
    }

    private static (string, int) Key(string host, int port) => (host.ToLowerInvariant(), port);
}
