using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Database.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Client.Infrastructure.Tests;

public sealed class StorageCompositionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StorageUsesSuppliedDirectoryAndReopensExistingDatabase(bool throughInfrastructure)
    {
        var directory = Directory.CreateTempSubdirectory("flowrate-composition-").FullName;
        try
        {
            using (var provider = CreateProvider(directory, throughInfrastructure))
            {
                Assert.Equal(DatabaseState.NoError,
                    await provider.GetRequiredService<IDatabaseInitializer>().VerifyDatabaseAsync());
                Assert.False(provider.GetRequiredService<IDatabaseProtection>().IsEncrypted);

                // Every repository port must be usable through either composition entry point.
                foreach (var port in typeof(IUserNotesRepository).Assembly.GetTypes().Where(type =>
                             type.IsInterface && type.Namespace == typeof(IUserNotesRepository).Namespace))
                    Assert.NotNull(provider.GetRequiredService(port));

                await provider.GetRequiredService<IUserNotesRepository>().SetAsync("server", 42, "saved note");
                Assert.True(File.Exists(Path.Combine(directory, "client.db")));
            }

            using (var provider = CreateProvider(directory, throughInfrastructure))
            {
                Assert.Equal(DatabaseState.NoError,
                    await provider.GetRequiredService<IDatabaseInitializer>().VerifyDatabaseAsync());
                Assert.Equal("saved note",
                    await provider.GetRequiredService<IUserNotesRepository>().GetAsync("server", 42));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ServiceProvider CreateProvider(string directory, bool throughInfrastructure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        if (throughInfrastructure)
            services.AddInfrastructure(directory);
        else
            services.AddSqliteDatabase(directory);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
