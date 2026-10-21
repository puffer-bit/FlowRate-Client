using Xunit;
using Client.Core.Coordinator;
using Client.Infrastructure.Database.Repositories;

namespace Client.Infrastructure.Tests;

public class TrustedCertificatesRepositoryTests
{
    private static readonly DateTime NotAfter = new(2027, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    [Fact]
    public async Task CertificateRoundTripsWithANormalizedHost()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new TrustedCertificatesRepository(database.Database);

        await repository.SaveAsync(new ServerCertificateModel(" Server.Local ", 443, "ab:cd", "CN=server", NotAfter));
        var certificate = await repository.GetAsync("SERVER.local", 443);

        Assert.NotNull(certificate);
        Assert.Equal("server.local", certificate.Host);
        Assert.Equal("AB:CD", certificate.Fingerprint);
        Assert.Equal("CN=server", certificate.Subject);
        Assert.Equal(NotAfter, certificate.NotAfter);
        Assert.Equal(DateTimeKind.Utc, certificate.NotAfter.Kind);
        Assert.Null(certificate.CoordinatorId);
    }

    [Fact]
    public async Task TrustingAnotherCertificateReplacesTheBinding()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new TrustedCertificatesRepository(database.Database);
        await repository.SaveAsync(new ServerCertificateModel("server", 443, "AA", "CN=old", NotAfter));
        await repository.BindCoordinatorAsync("server", 443, "coordinator");

        await repository.SaveAsync(new ServerCertificateModel("server", 443, "BB", "CN=new", NotAfter));
        var certificate = Assert.Single(await repository.GetAllAsync());

        Assert.Equal("BB", certificate.Fingerprint);
        Assert.Null(certificate.CoordinatorId);
    }

    [Fact]
    public async Task BindingAndRemoving()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new TrustedCertificatesRepository(database.Database);
        await repository.SaveAsync(new ServerCertificateModel("server", 443, "AA", "CN=server", NotAfter));

        await repository.BindCoordinatorAsync("Server", 443, "coordinator");

        Assert.Equal("coordinator", (await repository.GetAsync("server", 443))?.CoordinatorId);

        await repository.RemoveAsync("SERVER", 443);

        Assert.Null(await repository.GetAsync("server", 443));
        Assert.Empty(await repository.GetAllAsync());
    }
}
