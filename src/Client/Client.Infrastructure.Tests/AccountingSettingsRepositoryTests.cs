using Xunit;
using Client.Core.Settings;
using Client.Core.Users;
using Client.Infrastructure.Database.Repositories;
using Microsoft.Data.Sqlite;

namespace Client.Infrastructure.Tests;

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
        var byAddress = await repository.GetCoordinatorAsync("server.local", 5000);
        var byId = await repository.GetCoordinatorAsync("coordinator");

        Assert.Equal(2, all.Single(c => c.CoordinatorId == "coordinator").Users.Count);
        Assert.Empty(all.Single(c => c.CoordinatorId == "other").Users);
        Assert.NotNull(byAddress);
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
    public async Task SessionTokenAndNewIdentity_AreKeptWithTheSavedIdentity()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new AccountingSettingsRepository(database.Database);
        await repository.SaveCoordinatorAsync(Coordinator());
        await repository.SaveUserAsync(Identity(1));

        await repository.SetSessionTokenAsync("coordinator", 1, "token");
        await repository.SetUserIdentityAsync("coordinator", 1, "#new");
        await repository.SetSessionTokenAsync("coordinator", 2, "not saved");

        var user = Assert.Single((await repository.GetCoordinatorAsync("coordinator"))!.Users);
        Assert.Equal(("#new", "token"), (user.UserIdentity, user.SessionToken));

        await repository.SetSessionTokenAsync("coordinator", 1, null);
        Assert.Null((await repository.GetCoordinatorInstanceAsync(("coordinator", 1)))!.Value.User.SessionToken);
    }
}
