using Xunit;
using Client.Core.Push;
using Client.Infrastructure.Database.Repositories;

namespace Client.Infrastructure.Tests;

public class PushKeysRepositoryTests
{
    private static PushKey Key(string keyId, string coordinator, byte fill = 1) =>
        new(keyId, coordinator, Enumerable.Repeat(fill, 32).ToArray());

    [Fact]
    public async Task KeyIsFoundByItsCoordinatorAndByItsId()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new PushKeysRepository(database.Database);

        await repository.SaveAsync(Key("ABCDEF", "coordinator", 7));

        var byCoordinator = await repository.GetByCoordinatorAsync("coordinator");
        var byId = await repository.GetByKeyIdAsync("abcdef");

        Assert.NotNull(byCoordinator);
        Assert.Equal("abcdef", byCoordinator.KeyId);
        Assert.Equal(Enumerable.Repeat((byte)7, 32), byCoordinator.Key);
        Assert.Equal("coordinator", byId?.CoordinatorInstanceId);
        Assert.Null(await repository.GetByCoordinatorAsync("other"));
        Assert.Null(await repository.GetByKeyIdAsync("ffff"));
    }

    [Fact]
    public async Task SavingAgainReplacesTheKeyOfTheCoordinator()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new PushKeysRepository(database.Database);
        await repository.SaveAsync(Key("aa", "coordinator", 1));

        await repository.SaveAsync(Key("bb", "coordinator", 2));

        Assert.Null(await repository.GetByKeyIdAsync("aa"));
        Assert.Equal(2, (await repository.GetByCoordinatorAsync("coordinator"))!.Key[0]);
    }

    [Fact]
    public async Task RemovingLeavesTheKeysOfOtherCoordinators()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new PushKeysRepository(database.Database);
        await repository.SaveAsync(Key("aa", "one"));
        await repository.SaveAsync(Key("bb", "two"));

        await repository.RemoveAsync("one");

        Assert.Null(await repository.GetByKeyIdAsync("aa"));
        Assert.NotNull(await repository.GetByKeyIdAsync("bb"));
    }

    [Fact]
    public async Task ReplyTargetIsKeptWithTheKeyAndGoesWithIt()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        var repository = new PushKeysRepository(database.Database);
        await repository.SaveAsync(Key("aa", "coordinator"));
        Assert.Null(await repository.GetReplyTargetAsync("coordinator"));

        var target = new PushReplyTarget("server", 4430, "reply-token");
        await repository.SetReplyTargetAsync("coordinator", target);
        // The key is saved again on every registration; that does not lose the target
        await repository.SaveAsync(Key("aa", "coordinator"));

        Assert.Equal(target, await repository.GetReplyTargetAsync("coordinator"));

        await repository.RemoveAsync("coordinator");
        Assert.Null(await repository.GetReplyTargetAsync("coordinator"));
    }
}
