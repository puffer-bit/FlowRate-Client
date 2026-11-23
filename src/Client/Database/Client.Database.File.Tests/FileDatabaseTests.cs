using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Core.Coordinator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Client.Database.File.Tests;

public sealed class FileDatabaseTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("flowrate-file-db-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void MissingPhraseDoesNotCreateTheStorageDirectory()
    {
        var directory = Path.Combine(_directory, "not-created");
        using var provider = CreateProvider(directory);
        Assert.Null(provider.GetRequiredService<IRecoveryPhraseStore>().Load("localhost-26666-1"));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void ReadsTheExistingBotFileFormat()
    {
        System.IO.File.WriteAllText(Path.Combine(_directory, "phrase-localhost-26666-1.txt"), "  test phrase\n");
        using var provider = CreateProvider();
        Assert.Equal("test phrase", provider.GetRequiredService<IRecoveryPhraseStore>().Load("localhost-26666-1"));
    }

    [Fact]
    public void ReopenedStoreKeepsProfilesSeparate()
    {
        using (var provider = CreateProvider())
        {
            var store = provider.GetRequiredService<IRecoveryPhraseStore>();
            store.Save("localhost-26666-1", "first test phrase");
            store.Save("localhost-26666-2", "second test phrase");
            store.Save("localhost-26667-1", "other port test phrase");
            store.Save("other-host-26666-1", "other host test phrase");
        }

        using var reopened = CreateProvider();
        var phrases = reopened.GetRequiredService<IRecoveryPhraseStore>();
        Assert.Equal("first test phrase", phrases.Load("localhost-26666-1"));
        Assert.Equal("second test phrase", phrases.Load("localhost-26666-2"));
        Assert.Equal("other port test phrase", phrases.Load("localhost-26667-1"));
        Assert.Equal("other host test phrase", phrases.Load("other-host-26666-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MissingNewPhraseDoesNotOverwriteTheSavedAccount(string? phrase)
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<IRecoveryPhraseStore>();
        store.Save("localhost-26666-1", "saved test phrase");
        store.Save("localhost-26666-1", phrase);
        Assert.Equal("saved test phrase", store.Load("localhost-26666-1"));
    }

    [Fact]
    public void WriteFailureDoesNotStopTheBot()
    {
        var directory = Path.Combine(_directory, "occupied-by-a-file");
        System.IO.File.WriteAllText(directory, "occupied");
        using var provider = CreateProvider(directory);
        provider.GetRequiredService<IRecoveryPhraseStore>().Save("localhost-26666-1", "test phrase");
        Assert.Equal("occupied", System.IO.File.ReadAllText(directory));
    }

    [Fact]
    public void SaveCreatesTheHostSuppliedDirectory()
    {
        var directory = Path.Combine(_directory, "new", "nested");
        using var provider = CreateProvider(directory);
        provider.GetRequiredService<IRecoveryPhraseStore>().Save("localhost-26666-1", "test phrase");
        Assert.Equal("test phrase", System.IO.File.ReadAllText(Path.Combine(directory, "phrase-localhost-26666-1.txt")));
    }

    [Theory]
    [InlineData("../other")]
    [InlineData("..\\other")]
    public void ProfileCannotSelectADifferentDirectory(string profileId)
    {
        using var provider = CreateProvider();
        var store = provider.GetRequiredService<IRecoveryPhraseStore>();
        Assert.Throws<ArgumentException>(() => store.Save(profileId, "test phrase"));
        Assert.Throws<ArgumentException>(() => store.Load(profileId));
    }

    [Fact]
    public async Task CertificatesAreSharedWithinTheHostAndForgottenWhenItRestarts()
    {
        using (var provider = CreateProvider())
        {
            var certificates = provider.GetRequiredService<ITrustedCertificatesRepository>();
            var certificate = new ServerCertificateModel("LOCALHOST", 26666, "fingerprint", "test certificate",
                DateTimeOffset.UtcNow.AddDays(1));
            await certificates.SaveAsync(certificate);
            await certificates.BindCoordinatorAsync("localhost", 26666, "server");
            var anotherConsumer = provider.GetRequiredService<ITrustedCertificatesRepository>();
            Assert.Same(certificates, anotherConsumer);
            Assert.Equal(certificate with { CoordinatorId = "server" }, await anotherConsumer.GetAsync("localhost", 26666));
            Assert.Null(await anotherConsumer.GetAsync("localhost", 26667));
            Assert.Single(await certificates.GetAllAsync());
        }

        using var restarted = CreateProvider();
        Assert.Empty(await restarted.GetRequiredService<ITrustedCertificatesRepository>().GetAllAsync());
    }

    [Fact]
    public async Task CertificateCanBeReplacedAndRemoved()
    {
        using var provider = CreateProvider();
        var certificates = provider.GetRequiredService<ITrustedCertificatesRepository>();
        var original = new ServerCertificateModel("localhost", 26666, "old", "test certificate",
            DateTimeOffset.UtcNow.AddDays(1));
        await certificates.SaveAsync(original);
        await certificates.BindCoordinatorAsync("localhost", 26666, "old-server");
        var replacement = original with { Fingerprint = "new" };
        await certificates.SaveAsync(replacement);
        Assert.Equal(replacement, await certificates.GetAsync("localhost", 26666));
        await certificates.RemoveAsync("LOCALHOST", 26666);
        Assert.Empty(await certificates.GetAllAsync());
    }

    private ServiceProvider CreateProvider(string? directory = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddFileDatabase(directory ?? _directory);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
