using System.Reactive.Linq;
using Avalonia.Threading;
using Client.ViewModels.Connections;
using Client.Application.Settings;
using Client.Core.Coordinator;
using Client.Core.Settings;
using Client.ViewModels.Settings.Pages;
using Xunit;

namespace Client.Application.Tests;

public class BehaviorAutoConnectionTests
{
    [Fact]
    public async Task LoadingRestoresExactIdentityWithoutChangingSavedTarget()
    {
        var server = Server("first", 1, 2);
        var behavior = new BehaviorStub { Target = (server, server.Users[1]) };
        var vm = new BehaviorControlVM(behavior, new AccountingStub([server, Server("empty")]));

        await vm.LoadCommand.Execute();

        Assert.Same(server, vm.SelectedServer);
        Assert.Same(server.Users[1], vm.SelectedIdentity);
        Assert.Single(vm.SavedServers);
        Assert.Empty(behavior.Writes);
    }

    [Fact]
    public async Task ChangingServerReplacesIdentityEvenWhenUserIdsMatch()
    {
        var first = Server("first", 1);
        var second = Server("second", 1, 2);
        var behavior = new BehaviorStub { Target = (first, first.Users[0]) };
        var vm = new BehaviorControlVM(behavior, new AccountingStub([first, second]));
        await vm.LoadCommand.Execute();

        vm.SelectedServer = second;
        Assert.Same(second.Users[0], vm.SelectedIdentity);
        Assert.Equal(("second", 1), behavior.Writes.Last());

        vm.SelectedIdentity = second.Users[1];
        Assert.Equal(("second", 2), behavior.Writes.Last());

        // Reopening the tab restores the persisted pair without writing it again.
        var count = behavior.Writes.Count;
        await vm.LoadCommand.Execute();
        Assert.Same(second.Users[1], vm.SelectedIdentity);
        Assert.Equal(count, behavior.Writes.Count);
    }

    [Fact]
    public async Task MissingTargetLeavesSelectionEmptyUntilUserChoosesServer()
    {
        var server = Server("first", 1);
        var behavior = new BehaviorStub();
        var vm = new BehaviorControlVM(behavior, new AccountingStub([server]));
        await vm.LoadCommand.Execute();

        Assert.Null(vm.SelectedServer);
        Assert.Null(vm.SelectedIdentity);
        Assert.Empty(behavior.Writes);
        vm.SelectedServer = server;
        Assert.Equal(("first", 1), Assert.Single(behavior.Writes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoginWindowSelectsLastConnectedServerIndependentlyOfAutoConnect(bool hasAutoConnect)
    {
        var startup = Server("startup", 1);
        var latest = new SavedCoordinatorModel
        {
            CoordinatorId = "latest", Address = "latest.local", Port = 27777, SaveOrder = 3,
            Users = [new SavedUserModel { CoordinatorId = "latest", Id = 2, Username = "Alice" }]
        };
        var behavior = new BehaviorStub { Target = hasAutoConnect ? (startup, startup.Users[0]) : null };
        ConnectWindowVM? vm = null;
        await TestUiThread.RunAsync(() => vm = new ConnectWindowVM(
            new AccountingStub([latest, startup]), null!, behavior));
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var loaded = false;
            await TestUiThread.RunAsync(() =>
            {
                Dispatcher.UIThread.RunJobs();
                loaded = vm!.SelectedSavedServer != null && !vm.IsLoadingIdentities;
            });
            if (loaded) break;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        await TestUiThread.RunAsync(() =>
        {
            Assert.Equal("latest", vm!.SelectedSavedServer?.CoordinatorId);
            Assert.Equal("latest.local", vm.ServerAddress);
            Assert.Equal("27777", vm.Port);
            Assert.Equal(2, vm.SelectedIdentity?.Id);
            Assert.Empty(behavior.Writes);
            Assert.Equal(hasAutoConnect ? "startup" : null, behavior.Target?.Coordinator.CoordinatorId);
            vm.CancelTasks();
        });
    }

    private static SavedCoordinatorModel Server(string id, params int[] users) => new()
    {
        CoordinatorId = id, Address = "localhost", Port = 26666,
        Users = users.Select(user => new SavedUserModel { CoordinatorId = id, Id = user, Username = $"User {user}" }).ToList()
    };

    private sealed class BehaviorStub : IBehaviorService
    {
        public (SavedCoordinatorModel Coordinator, SavedUserModel User)? Target { get; set; }
        public List<(string, int)?> Writes { get; } = [];
        public Task<(SavedCoordinatorModel Coordinator, SavedUserModel User)?> GetAutoConnectInstance() => Task.FromResult(Target);
        public Task SetLastIdentityAsync((string CoordinatorId, int UserId) identity)
        {
            Writes.Add(identity);
            Target = (Server(identity.CoordinatorId, identity.UserId), new SavedUserModel { Id = identity.UserId });
            return Task.CompletedTask;
        }
        public Task RemoveLastIdentityAsync() { Writes.Add(null); Target = null; return Task.CompletedTask; }
        public Task<BehaviorSettings> GetSettingsAsync() => Task.FromResult(new BehaviorSettings());
        public Task SaveSettingsAsync(BehaviorSettings settings) => Task.CompletedTask;
    }

    private sealed class AccountingStub(IReadOnlyList<SavedCoordinatorModel> servers) : IAccountingService
    {
        public Task<IReadOnlyList<SavedCoordinatorModel>> GetAllSavedCoordinatorsAsync() => Task.FromResult(servers);
        public Task<IReadOnlyList<SavedCoordinatorModel>> GetSavedCoordinatorsAsync(string address, int port) =>
            Task.FromResult<IReadOnlyList<SavedCoordinatorModel>>(servers.Where(server => server.Address == address && server.Port == port).ToArray());
        public Task RemoveUserIdentityAsync(string coordinatorId, int userId) => throw new NotSupportedException();
        public Task SaveCoordinatorInstanceAsync(SavedCoordinatorModel instanceModel, SavedUserModel userModel) => throw new NotSupportedException();
        public Task RemoveCoordinatorInstanceAsync(string coordinatorId) => throw new NotSupportedException();
        public Task SetAccountPasswordAsync(string coordinatorId, int userId, string? accountPassword) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServerCertificateModel>> GetTrustedCertificatesAsync() => throw new NotSupportedException();
        public Task RemoveTrustedCertificateAsync(string host, int port) => throw new NotSupportedException();
    }
}
