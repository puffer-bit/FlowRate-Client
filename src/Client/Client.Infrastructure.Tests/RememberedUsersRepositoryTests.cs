using Xunit;
using Client.Core.Users;
using Client.Infrastructure.Database.Repositories;
using Shared.Enums.User;

namespace Client.Infrastructure.Tests;

public class RememberedUsersRepositoryTests
{
    private static UserModel User(int id, string username = "user") => new()
    {
        Id = id,
        CoordinatorInstanceId = "instance",
        Username = username,
        State = UserState.Suspended,
        Status = UserStatus.Away,
        FirstConnectionTime = DateTime.UtcNow,
        LastConnectionTime = DateTime.UtcNow
    };

    [Fact]
    public async Task UnknownUserGetsTheDefaults()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new RememberedUsersRepository(database.Database);

        var data = await repository.GetHybridSessionDataAsync("instance", 1);

        Assert.Equal(100, data.Volume);
        Assert.False(data.IsMuted);
    }

    [Fact]
    public async Task HybridSessionDataNeedsARememberedUser()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new RememberedUsersRepository(database.Database);

        Assert.False(await repository.SaveHybridSessionDataAsync("instance", 1, new UserHybridSessionData()));

        await repository.SaveUserAsync(User(1));

        Assert.True(await repository.SaveHybridSessionDataAsync("instance", 1,
            new UserHybridSessionData
            {
                Volume = 40, IsVolumeOverrideEnabled = true, IsMuted = true, IsBlocked = true,
                ScreenVolume = 130, IsScreenVolumeOverrideEnabled = true, IsScreenMuted = true
            }));

        var data = await repository.GetHybridSessionDataAsync("instance", 1);
        Assert.Equal(40, data.Volume);
        Assert.True(data.IsVolumeOverrideEnabled);
        Assert.True(data.IsMuted);
        Assert.True(data.IsBlocked);
        Assert.Equal(130, data.ScreenVolume);
        Assert.True(data.IsScreenVolumeOverrideEnabled);
        Assert.True(data.IsScreenMuted);
    }

    [Fact]
    public async Task UpdatingTheProfileKeepsTheHybridSessionData()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new RememberedUsersRepository(database.Database);
        await repository.SaveUserAsync(User(1, "before"));
        await repository.SaveHybridSessionDataAsync("instance", 1, new UserHybridSessionData { Volume = 40 });

        await repository.SaveUserAsync(User(1, "after"));

        Assert.Equal(40, (await repository.GetHybridSessionDataAsync("instance", 1)).Volume);
        Assert.Equal("after", await database.ScalarAsync("SELECT Username FROM RememberedUsers WHERE UserId = 1"));
    }

    [Fact]
    public async Task SeveralUsersInOneQuery()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new RememberedUsersRepository(database.Database);
        await repository.SaveUserAsync(User(1));
        await repository.SaveHybridSessionDataAsync("instance", 1, new UserHybridSessionData { Volume = 40 });

        var data = await repository.GetHybridSessionDataAsync("instance", [1, 2, 1]);

        Assert.Equal(2, data.Count);
        Assert.Equal(40, data[1].Volume);
        Assert.Equal(100, data[2].Volume);
    }

    [Fact]
    public async Task UserIdsAreScopedByCoordinatorInstance()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new RememberedUsersRepository(database.Database);
        await repository.SaveUserAsync(User(1));
        await repository.SaveHybridSessionDataAsync("instance", 1, new UserHybridSessionData { Volume = 40 });

        Assert.Equal(100, (await repository.GetHybridSessionDataAsync("other", 1)).Volume);
    }
}
