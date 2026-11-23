using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Client.Database.File;

public static class DependencyInjection
{
    /// <summary>Registers plaintext recovery phrase files and certificates trusted for the lifetime of the process.</summary>
    public static IServiceCollection AddFileDatabase(this IServiceCollection services, string databaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseDirectory);

        services.AddSingleton<IRecoveryPhraseStore>(provider => new FileRecoveryPhraseStore(databaseDirectory,
            provider.GetRequiredService<ILogger<FileRecoveryPhraseStore>>()));
        services.AddSingleton<ITrustedCertificatesRepository, InMemoryTrustedCertificates>();
        return services;
    }
}
