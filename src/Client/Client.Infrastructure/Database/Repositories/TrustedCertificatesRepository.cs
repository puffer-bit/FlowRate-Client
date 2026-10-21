using Client.Application.Ports.Repositories;
using Client.Core.Coordinator;
using Dapper;

namespace Client.Infrastructure.Database.Repositories;

internal sealed class TrustedCertificatesRepository : ITrustedCertificatesRepository
{
    private const string SelectCertificates =
        "SELECT Host, Port, Fingerprint, Subject, NotAfter, CoordinatorId FROM TrustedCertificates";

    private readonly SqliteDatabase _database;

    public TrustedCertificatesRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<ServerCertificateModel?> GetAsync(string host, int port)
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QueryFirstOrDefaultAsync<TrustedCertificateRow>(
            SelectCertificates + " WHERE Host = @host AND Port = @port", new { host = NormalizeHost(host), port });

        return row?.ToModel();
    }

    public async Task<IReadOnlyList<ServerCertificateModel>> GetAllAsync()
    {
        await using var connection = await _database.OpenAsync();

        var rows = await connection.QueryAsync<TrustedCertificateRow>(SelectCertificates + " ORDER BY Host, Port");

        return rows.Select(row => row.ToModel()).ToList();
    }

    public async Task SaveAsync(ServerCertificateModel certificate)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO TrustedCertificates (Host, Port, Fingerprint, Subject, NotAfter, TrustedAt, CoordinatorId)
            VALUES (@Host, @Port, @Fingerprint, @Subject, @NotAfter, @TrustedAt, @CoordinatorId)
            ON CONFLICT (Host, Port) DO UPDATE SET
                Fingerprint = excluded.Fingerprint,
                Subject = excluded.Subject,
                NotAfter = excluded.NotAfter,
                TrustedAt = excluded.TrustedAt,
                CoordinatorId = excluded.CoordinatorId
            """, new
            {
                Host = NormalizeHost(certificate.Host),
                certificate.Port,
                Fingerprint = certificate.Fingerprint.ToUpperInvariant(),
                certificate.Subject,
                NotAfter = SqliteDatabase.ToStorage(certificate.NotAfter),
                TrustedAt = DateTime.UtcNow,
                certificate.CoordinatorId
            });
    }

    public async Task BindCoordinatorAsync(string host, int port, string coordinatorId)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync(
            "UPDATE TrustedCertificates SET CoordinatorId = @coordinatorId WHERE Host = @host AND Port = @port",
            new { coordinatorId, host = NormalizeHost(host), port });
    }

    public async Task RemoveAsync(string host, int port)
    {
        await using var connection = await _database.OpenAsync();

        await connection.ExecuteAsync(
            "DELETE FROM TrustedCertificates WHERE Host = @host AND Port = @port",
            new { host = NormalizeHost(host), port });
    }

    // Host names are case insensitive, so "Server" and "server" share one trusted certificate
    private static string NormalizeHost(string host) => host.Trim().ToLowerInvariant();
}

internal sealed record TrustedCertificateRow(string Host, int Port, string Fingerprint, string Subject,
    DateTime NotAfter, string? CoordinatorId)
{
    public ServerCertificateModel ToModel() =>
        new(Host, Port, Fingerprint, Subject, SqliteDatabase.FromStorage(NotAfter), CoordinatorId);
}
