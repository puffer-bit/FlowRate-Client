using Xunit;
using Client.Core.Settings;
using Client.Application.Settings;
using Client.Core.Users;
using Client.Database.Sqlite.Repositories;
using Microsoft.Data.Sqlite;

namespace Client.Database.Sqlite.Tests;

[Collection(DatabaseTestCollection.Name)]
public class AccountingSettingsRepositoryTests
{
    private static SavedCoordinatorModel Coordinator(string id = "coordinator", string? name = "Main") => new()
    {
        CoordinatorId = id,
        Name = name,
        Address = "server.local",
        Port = 5000
    };

    private static SavedUserModel Identity(int userId, string identity = "identity", string? password = null) => new()
    {
        CoordinatorId = "coordinator",
        Id = userId,
        UserIdentity = identity,
        Username = $"user{userId}",
        Password = password
    };

    private static AccountingService Service(TestDatabase database, AccountingSettingsRepository repository)
    {
        var secrets = new SecretsRepository(database.Database);
        var vault = new Client.Application.Security.SecretVault(new NoKeyring(), secrets,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Client.Application.Security.SecretVault>.Instance);
        return new AccountingService(repository, new TrustedCertificatesRepository(database.Database), vault,
            new Client.Application.Security.VaultDeviceKeyStore(vault),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AccountingService>.Instance);
    }

    [Fact]
    public async Task ServerPassword_MovesFromTheDatabaseToTheVault()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AccountingSettingsRepository(database.Database);
        await repository.SaveCoordinatorAsync(Coordinator());
        await repository.SaveUserAsync(Identity(1));
        // As the earlier client left it
        await using (var connection = await database.Database.OpenAsync())
        {
            var command = connection.CreateCommand();
            command.CommandText = "UPDATE SavedIdentities SET Password = 'server-secret'";
            await command.ExecuteNonQueryAsync();
        }

        var service = Service(database, repository);
        var user = Assert.Single(Assert.Single(await service.GetAllSavedCoordinatorsAsync()).Users);

        Assert.Equal("server-secret", user.Password);
        Assert.Null((await repository.GetCoordinatorInstanceAsync(("coordinator", 1)))!.Value.User.Password);
        Assert.Equal("server-secret",
            Assert.Single(Assert.Single(await service.GetAllSavedCoordinatorsAsync()).Users).Password);
    }

    [Fact]
    public async Task SavingACoordinatorAgainUpdatesIt()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AccountingSettingsRepository(database.Database);

        await repository.SaveCoordinatorAsync(Coordinator(name: "Old"));
        await repository.SaveCoordinatorAsync(Coordinator(name: "New"));

        var coordinator = Assert.Single(await repository.GetAllCoordinatorsAsync());
        Assert.Equal("New", coordinator.Name);
    }

    [Fact]
    public async Task SavingASuccessfulConnectionMakesThatServerTheMostRecent()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AccountingSettingsRepository(database.Database);
        await repository.SaveCoordinatorAsync(Coordinator("first"));
        await repository.SaveCoordinatorAsync(Coordinator("second"));
        var servers = await repository.GetAllCoordinatorsAsync();
        Assert.Equal("second", servers.MaxBy(server => server.SaveOrder)!.CoordinatorId);
        var previousOrder = servers.Max(server => server.SaveOrder);

        await repository.SaveCoordinatorAsync(Coordinator("first"));
        servers = await repository.GetAllCoordinatorsAsync();
        var latest = servers.MaxBy(server => server.SaveOrder)!;
        Assert.Equal("first", latest.CoordinatorId);
        Assert.True(latest.SaveOrder > previousOrder);
        Assert.Equal(latest.SaveOrder, (await repository.GetCoordinatorAsync("first"))!.SaveOrder);
    }

    [Fact]
    public async Task CoordinatorsAtTheSameEndpointKeepSeparateIdentities()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AccountingSettingsRepository(database.Database);
        await repository.SaveCoordinatorAsync(Coordinator());
        await repository.SaveUserAsync(Identity(7, "old-identity"));
        await repository.SaveCoordinatorAsync(Coordinator("replacement"));
        await repository.SaveUserAsync(new SavedUserModel
        {
            CoordinatorId = "replacement",
            Id = 7,
            UserIdentity = "new-identity",
            Username = "new-user",
            SessionToken = "new-token"
        });

        var service = Service(database, repository);
        var matches = await service.GetSavedCoordinatorsAsync("SERVER.LOCAL", 5000);
        Assert.Equal(2, matches.Count);
        var coordinator = matches.Single(c => c.CoordinatorId == "replacement");

        Assert.NotNull(coordinator);
        Assert.Equal("replacement", coordinator.CoordinatorId);
        var user = Assert.Single(coordinator.Users);
        Assert.Equal("new-identity", user.UserIdentity);
        Assert.Equal("new-token", user.SessionToken);
        var oldCoordinator = await repository.GetCoordinatorAsync("coordinator");
        Assert.Equal("old-identity", Assert.Single(oldCoordinator!.Users).UserIdentity);

        // Saving either coordinator again preserves both accounts at the shared endpoint.
        await repository.SaveCoordinatorAsync(Coordinator());
        coordinator = (await service.GetSavedCoordinatorsAsync("server.local", 5000)).Single(c => c.CoordinatorId == "coordinator");
        Assert.Equal("coordinator", coordinator!.CoordinatorId);
        Assert.Equal("old-identity", Assert.Single(coordinator.Users).UserIdentity);
    }

    [Fact]
    public async Task AccountsAreListedOnlyForTheirEndpoint()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AccountingSettingsRepository(database.Database);
        await repository.SaveCoordinatorAsync(Coordinator());
        await repository.SaveCoordinatorAsync(new SavedCoordinatorModel
        {
            CoordinatorId = "other-port",
            Address = "server.local",
            Port = 5001
        });
        await repository.SaveCoordinatorAsync(new SavedCoordinatorModel
        {
            CoordinatorId = "other-host",
            Address = "other.local",
            Port = 5000
        });

        var service = Service(database, repository);
        Assert.Equal("coordinator", Assert.Single(await service.GetSavedCoordinatorsAsync("server.local", 5000)).CoordinatorId);
        Assert.Equal("other-port", Assert.Single(await service.GetSavedCoordinatorsAsync("server.local", 5001)).CoordinatorId);
        Assert.Equal("other-host", Assert.Single(await service.GetSavedCoordinatorsAsync("other.local", 5000)).CoordinatorId);
        Assert.Empty(await service.GetSavedCoordinatorsAsync("missing.local", 5000));
    }

    [Fact]
    public async Task IdentityComesWithTheAvatarOfTheRememberedUser()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AccountingSettingsRepository(database.Database);
        await repository.SaveCoordinatorAsync(Coordinator());
        await repository.SaveUserAsync(Identity(1));
        await repository.SaveUserAsync(Identity(2));
        await new RememberedUsersRepository(database.Database).SaveUserAsync(new UserModel
        {
            Id = 1,
            CoordinatorInstanceId = "coordinator",
            Username = "user1",
            AvatarUrl = "/media/avatars/0a1b"
        });

        var users = (await repository.GetCoordinatorAsync("coordinator"))!.Users;

        Assert.Equal("/media/avatars/0a1b", users.Single(user => user.Id == 1).AvatarUrl);
        Assert.Null(users.Single(user => user.Id == 2).AvatarUrl);
    }

    [Fact]
    public async Task CoordinatorsComeWithTheirIdentities()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AccountingSettingsRepository(database.Database);
        await repository.SaveCoordinatorAsync(Coordinator());
        await repository.SaveCoordinatorAsync(Coordinator("other"));
        await repository.SaveUserAsync(Identity(1));
        await repository.SaveUserAsync(Identity(2));

        var all = await repository.GetAllCoordinatorsAsync();
        var byId = await repository.GetCoordinatorAsync("coordinator");

        Assert.Equal(2, all.Single(c => c.CoordinatorId == "coordinator").Users.Count);
        Assert.Empty(all.Single(c => c.CoordinatorId == "other").Users);
        Assert.Equal(2, byId!.Users.Count);
        Assert.Null(await repository.GetCoordinatorAsync("missing"));
    }

    [Fact]
    public async Task SavingAnIdentityAgainUpdatesIt()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AccountingSettingsRepository(database.Database);
        await repository.SaveCoordinatorAsync(Coordinator());

        await repository.SaveUserAsync(Identity(7, "first", "secret"));
        await repository.SaveUserAsync(Identity(7, "second", "changed"));

        var instance = await repository.GetCoordinatorInstanceAsync(("coordinator", 7));

        Assert.NotNull(instance);
        Assert.Equal(7, instance.Value.User.Id);
        Assert.Equal("second", instance.Value.User.UserIdentity);
        Assert.Equal("changed", instance.Value.User.Password);
        Assert.Equal("coordinator", instance.Value.Model.CoordinatorId);
    }

    [Fact]
    public async Task RenamingAnIdentityKeepsItsCredentials()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AccountingSettingsRepository(database.Database);
        await repository.SaveCoordinatorAsync(Coordinator());
        await repository.SaveUserAsync(Identity(7, "identity", "secret"));

        await repository.RenameUserAsync("coordinator", 7, "renamed");
        await repository.RenameUserAsync("coordinator", 8, "not saved");

        var user = Assert.Single((await repository.GetCoordinatorAsync("coordinator"))!.Users);
        Assert.Equal("renamed", user.Username);
        Assert.Equal("identity", user.UserIdentity);
        Assert.Equal("secret", user.Password);
    }

    [Fact]
    public async Task UnknownInstanceIsNull()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AccountingSettingsRepository(database.Database);
        await repository.SaveCoordinatorAsync(Coordinator());

        Assert.Null(await repository.GetCoordinatorInstanceAsync(("coordinator", 1)));
        Assert.Null(await repository.GetCoordinatorInstanceAsync(("missing", 1)));
    }

    [Fact]
    public async Task AnIdentityNeedsItsCoordinator()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AccountingSettingsRepository(database.Database);

        await Assert.ThrowsAsync<SqliteException>(() => repository.SaveUserAsync(Identity(1)));
    }

    [Fact]
    public async Task DeletingACoordinatorDeletesItsIdentities()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AccountingSettingsRepository(database.Database);
        await repository.SaveCoordinatorAsync(Coordinator());
        await repository.SaveUserAsync(Identity(1));

        await repository.DeleteCoordinatorAsync("coordinator");

        Assert.Empty(await repository.GetAllCoordinatorsAsync());
        Assert.Equal(0L, await database.ScalarAsync("SELECT COUNT(*) FROM SavedIdentities"));
    }


    [Fact]
    public async Task DatabaseSettingsRoundTrip()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AccountingSettingsRepository(database.Database);

        Assert.False((await repository.GetDatabaseSettingsAsync()).IsDatabaseEncryptionEnabled);

        await repository.UpdateDatabaseSettingsAsync(new DatabaseSettings
        {
            IsDatabaseEncryptionEnabled = true,
            IsDatabaseLockEnabled = true
        });

        var settings = await repository.GetDatabaseSettingsAsync();
        Assert.True(settings.IsDatabaseEncryptionEnabled);
        Assert.False(settings.IsDatabasePasswordSaveEnabled);
        Assert.True(settings.IsDatabaseLockEnabled);
    }

    [Fact]
    public async Task Device_IsKept_AndMovingOverDropsTheLegacyLogin()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AccountingSettingsRepository(database.Database);
        await repository.SaveCoordinatorAsync(Coordinator());
        await repository.SaveUserAsync(Identity(1));
        var deviceId = Guid.NewGuid();

        await repository.SaveUserAsync(new SavedUserModel
        {
            CoordinatorId = "coordinator", Id = 1, Username = "alice", DeviceId = deviceId, DeviceKey = "key",
            UserIdentity = "#old", SessionToken = "token", AccountPassword = "secret", Password = "server"
        });

        var user = Assert.Single((await repository.GetCoordinatorAsync("coordinator"))!.Users);
        Assert.Equal((deviceId, "key"), (user.DeviceId, user.DeviceKey));
        Assert.Equal("", user.UserIdentity);
        Assert.Null(user.SessionToken);
        Assert.Null(user.AccountPassword);
        // The server password lives in the secret vault, never in the database
        Assert.Null(user.Password);

        await repository.SetDeviceAsync("coordinator", 1, null, null);
        await repository.SetRecoveryPhraseSavedAsync("coordinator", 1, true);
        user = (await repository.GetCoordinatorInstanceAsync(("coordinator", 1)))!.Value.User;
        Assert.Null(user.DeviceId);
        Assert.True(user.IsRecoveryPhraseSaved);
        Assert.False(user.CanLogIn);
    }

    [Fact]
    public async Task Secrets_AreKeptInTheDatabaseWithoutAKeyring()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var secrets = new SecretsRepository(database.Database);

        await secrets.SaveAsync("name", "first");
        await secrets.SaveAsync("name", "second");
        Assert.Equal("second", await secrets.LoadAsync("name"));

        await secrets.DeleteAsync("name");
        Assert.Null(await secrets.LoadAsync("name"));
    }

    [Fact]
    public async Task DeletingOneIdentity_PreservesCoordinatorAndOtherAccountsAndClearsItsAutoConnect()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AccountingSettingsRepository(database.Database);
        var behavior = new BehaviorSettingsRepository(database.Database);
        await repository.SaveCoordinatorAsync(Coordinator());
        await repository.SaveCoordinatorAsync(Coordinator("other"));
        await repository.SaveUserAsync(Identity(1));
        await repository.SaveUserAsync(Identity(2));
        await repository.SaveUserAsync(new SavedUserModel { CoordinatorId = "other", Id = 1, UserIdentity = "other", Username = "other" });
        await behavior.SetAutoConnectInstanceAsync(("coordinator", 1));

        await repository.DeleteUserAsync("coordinator", 1);

        var coordinator = await repository.GetCoordinatorAsync("coordinator");
        Assert.Equal(2, Assert.Single(coordinator!.Users).Id);
        Assert.Single((await repository.GetCoordinatorAsync("other"))!.Users);
        Assert.Null(await behavior.GetAutoConnectInstanceAsync());

        await behavior.SetAutoConnectInstanceAsync(("other", 1));
        await repository.DeleteUserAsync("coordinator", 2);
        Assert.Empty((await repository.GetCoordinatorAsync("coordinator"))!.Users);
        Assert.Equal(("other", 1), await behavior.GetAutoConnectInstanceAsync());
    }
}

/// <summary>A system without a keyring: secrets fall back to the database.</summary>
internal sealed class NoKeyring : Client.Application.Ports.ISecureStorage
{
    public bool IsAvailable => false;
    public Task SavePassword(string password, string username, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task<string?> LoadPassword(string username, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task DeletePassword(string username, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public void Dispose()
    {
    }
}
