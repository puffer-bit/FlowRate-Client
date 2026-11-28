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
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unsupported")]
    [InlineData("👍")]
    public async Task QuickReactionSelectionAlwaysSavesSupportedValue(string? selection)
    {
        var behavior = new BehaviorStub();
        var vm = new BehaviorControlVM(behavior, new AccountingStub([]));
        await vm.LoadCommand.Execute();

        vm.QuickReaction = selection;

        Assert.Equal(selection == "👍" ? selection : BehaviorSettings.DefaultQuickReaction,
            behavior.Settings.QuickReaction);
    }

    [Fact]
    public async Task ScreenAwakePreferenceLoadsSavesAndResetsWithBehaviorSettings()
    {
        var behavior = new BehaviorStub { Settings = new() { KeepScreenOnWhileWatching = false } };
        var vm = new BehaviorControlVM(behavior, new AccountingStub([]));

        await vm.LoadCommand.Execute();
        Assert.False(vm.KeepScreenOnWhileWatching);
        vm.KeepScreenOnWhileWatching = true;
        Assert.True(behavior.Settings.KeepScreenOnWhileWatching);
        vm.KeepScreenOnWhileWatching = false;
        Assert.False(behavior.Settings.KeepScreenOnWhileWatching);
        await vm.ResetAllAsync();
        Assert.True(vm.KeepScreenOnWhileWatching);
        Assert.True(behavior.Settings.KeepScreenOnWhileWatching);
        Assert.Contains(vm.SearchEntries, entry => entry.Key == "behavior.keepScreenOn");
    }

    [Fact]
    public async Task LoadingRestoresExactIdentityWithoutChangingSavedChoice()
    {
        var server = Server("first", 1, 2);
        var behavior = new BehaviorStub { Choice = new AutoConnectChoice("first", 2) };
        var vm = new BehaviorControlVM(behavior, new AccountingStub([server, Server("empty")]));

        await vm.LoadCommand.Execute();

        Assert.Same(server, vm.SelectedServer);
        Assert.Same(server.Users[1], vm.SelectedIdentity);
        Assert.True(vm.IsIdentitySelectable);
        // The last server first, then the servers with an account
        Assert.Equal([vm.LastServer, server], vm.SavedServers);
        Assert.Equal([vm.LastIdentity, ..server.Users], vm.Identities);
        Assert.Empty(behavior.Writes);
    }

    [Fact]
    public async Task ChangingServerStartsFromItsLastIdentity()
    {
        var first = Server("first", 1);
        var second = Server("second", 1, 2);
        var behavior = new BehaviorStub { Choice = new AutoConnectChoice("first", 1) };
        var vm = new BehaviorControlVM(behavior, new AccountingStub([first, second]));
        await vm.LoadCommand.Execute();

        vm.SelectedServer = second;
        Assert.Same(vm.LastIdentity, vm.SelectedIdentity);
        Assert.Equal(new AutoConnectChoice("second", null), behavior.Writes.Last());

        vm.SelectedIdentity = second.Users[1];
        Assert.Equal(new AutoConnectChoice("second", 2), behavior.Writes.Last());

        // Reopening the tab restores the persisted pair without writing it again.
        var count = behavior.Writes.Count;
        await vm.LoadCommand.Execute();
        Assert.Same(second.Users[1], vm.SelectedIdentity);
        Assert.Equal(count, behavior.Writes.Count);
    }

    [Fact]
    public async Task LastServerComesWithItsLastIdentityOnly()
    {
        var server = Server("first", 1);
        var behavior = new BehaviorStub { Choice = new AutoConnectChoice("first", 1) };
        var vm = new BehaviorControlVM(behavior, new AccountingStub([server]));
        await vm.LoadCommand.Execute();

        vm.SelectedServer = vm.LastServer;

        Assert.False(vm.IsIdentitySelectable);
        Assert.Equal([vm.LastIdentity], vm.Identities);
        Assert.Same(vm.LastIdentity, vm.SelectedIdentity);
        Assert.Equal(AutoConnectChoice.Last, Assert.Single(behavior.Writes));
    }

    [Fact]
    public async Task NothingPickedOrPickedAndGoneShowsTheLastOnes()
    {
        var server = Server("first", 1);
        foreach (var choice in new[] { AutoConnectChoice.Last, new AutoConnectChoice("gone", 4), new AutoConnectChoice("first", 9) })
        {
            var behavior = new BehaviorStub { Choice = choice };
            var vm = new BehaviorControlVM(behavior, new AccountingStub([server]));
            await vm.LoadCommand.Execute();

            Assert.Same(choice.CoordinatorId == "first" ? server : vm.LastServer, vm.SelectedServer);
            Assert.Same(vm.LastIdentity, vm.SelectedIdentity);
            Assert.Empty(behavior.Writes);
        }
    }

    [Fact]
    public async Task ResetFollowsTheLastConnectionAgain()
    {
        var server = Server("first", 1);
        var behavior = new BehaviorStub { Choice = new AutoConnectChoice("first", 1) };
        var vm = new BehaviorControlVM(behavior, new AccountingStub([server]));
        await vm.LoadCommand.Execute();

        await vm.ResetAllAsync();

        Assert.Same(vm.LastServer, vm.SelectedServer);
        Assert.Equal(AutoConnectChoice.Last, behavior.Choice);
        Assert.True(behavior.Settings.IsAutoConnectEnabled);
    }

    [Theory]
    [InlineData(null, "connect.identity.lost.noDevice")]
    [InlineData(LoginLostReason.Revoked, "connect.identity.lost.revoked")]
    [InlineData(LoginLostReason.NotFound, "connect.identity.lost.notFound")]
    [InlineData(LoginLostReason.LoggedOut, "connect.identity.lost.loggedOut")]
    public async Task ConnectWindowTellsWhyASavedAccountHasToLogInAgain(LoginLostReason? reason, string key)
    {
        await TestUiThread.RunAsync(() =>
        {
            var vm = new ConnectWindowVM(new AccountingStub([]), null!, new BehaviorStub());
            vm.SelectedIdentity = new SavedUserModel
            {
                CoordinatorId = "server", Id = 1, Username = "Alice", LoginLostReason = reason
            };
            Assert.Equal(Client.Avalonia.Localization.Strings.Get(key), vm.IdentityError);
            Assert.True(vm.IsLoginFormVisible);

            // An account that can log in, and the entries that are not accounts, show nothing
            vm.SelectedIdentity = new SavedUserModel
            {
                CoordinatorId = "server", Id = 1, Username = "Alice", DeviceId = Guid.NewGuid(), DeviceKey = "key"
            };
            Assert.Null(vm.IdentityError);
            vm.SelectedIdentity = vm.ExistingAccount;
            Assert.Null(vm.IdentityError);
            vm.CancelTasks();
        });
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

    [Fact]
    public async Task LoginWindowOpensOnTheAccountThatHasToLogInAgain()
    {
        var startup = new SavedCoordinatorModel
        {
            CoordinatorId = "startup", Address = "startup.local", Port = 26666, SaveOrder = 1,
            Users =
            [
                new SavedUserModel { CoordinatorId = "startup", Id = 1, Username = "Bob" },
                new SavedUserModel { CoordinatorId = "startup", Id = 3, Username = "Carol", LoginLostReason = LoginLostReason.Revoked }
            ]
        };
        var latest = new SavedCoordinatorModel
        {
            CoordinatorId = "latest", Address = "latest.local", Port = 27777, SaveOrder = 3,
            Users = [new SavedUserModel { CoordinatorId = "latest", Id = 2, Username = "Alice" }]
        };
        ConnectWindowVM? vm = null;
        await TestUiThread.RunAsync(() =>
        {
            vm = new ConnectWindowVM(new AccountingStub([latest, startup]), null!, new BehaviorStub());
            vm.PreferAccount("startup", 3);
        });
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var loaded = false;
            await TestUiThread.RunAsync(() =>
            {
                Dispatcher.UIThread.RunJobs();
                loaded = vm!.SelectedSavedServer != null && !vm.IsLoadingIdentities && vm.SelectedIdentity != null;
            });
            if (loaded) break;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        await TestUiThread.RunAsync(() =>
        {
            Assert.Equal("startup", vm!.SelectedSavedServer?.CoordinatorId);
            Assert.Equal(3, vm.SelectedIdentity?.Id);
            Assert.Equal(Client.Avalonia.Localization.Strings.Get("connect.identity.lost.revoked"), vm.IdentityError);
            vm.CancelTasks();
        });
    }

    [Fact]
    public async Task LoginLinkFillsTheFormWithItsServerAndCode()
    {
        ConnectWindowVM? vm = null;
        await TestUiThread.RunAsync(() =>
        {
            vm = new ConnectWindowVM(new AccountingStub([Server("saved", 1)]), null!, new BehaviorStub());
            vm.OpenLink(new Client.Application.Links.LoginLink("chat.example.com", 5000, null, "ABCD-EFGH-JKMN"));
        });
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var loaded = false;
            await TestUiThread.RunAsync(() =>
            {
                Dispatcher.UIThread.RunJobs();
                loaded = !vm!.IsLoadingIdentities && vm.SelectedIdentity != null;
            });
            if (loaded) break;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        await TestUiThread.RunAsync(() =>
        {
            Dispatcher.UIThread.RunJobs();
            // The saved server of the last connection does not take the place of the server of the link
            Assert.Equal("chat.example.com", vm!.ServerAddress);
            Assert.Equal("5000", vm.Port);
            Assert.Same(vm.ExistingAccount, vm.SelectedIdentity);
            Assert.True(vm.IsCodeLogin);
            Assert.Equal("ABCD-EFGH-JKMN", vm.LoginCodeText);
            vm.CancelTasks();
        });
    }

    [Fact]
    public async Task ScanOfANewDeviceDoesNotFillTheConnectForm()
    {
        await TestUiThread.RunAsync(() =>
        {
            var vm = new ConnectWindowVM(new AccountingStub([]), null!, new BehaviorStub());
            Assert.False(vm.OpenScanned("flowrate-link:ABCD-EFGH"));
            Assert.False(vm.OpenScanned("https://example.com"));
            Assert.True(vm.OpenScanned("flowrate://connect?host=chat.example.com"));
            Assert.Equal("chat.example.com", vm.ServerAddress);
            Assert.Null(vm.Port);
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
        public event Action<BehaviorSettings>? SettingsChanged { add { } remove { } }
        public BehaviorSettings Settings { get; set; } = new();
        public (SavedCoordinatorModel Coordinator, SavedUserModel User)? Target { get; set; }
        public AutoConnectChoice Choice { get; set; } = AutoConnectChoice.Last;
        public List<AutoConnectChoice> Writes { get; } = [];
        public List<(string, int)> Connections { get; } = [];
        public Task<(SavedCoordinatorModel Coordinator, SavedUserModel User)?> GetAutoConnectInstance() => Task.FromResult(Target);
        public Task<AutoConnectChoice> GetAutoConnectChoiceAsync() => Task.FromResult(Choice);
        public Task SetAutoConnectChoiceAsync(AutoConnectChoice choice)
        {
            Writes.Add(choice);
            Choice = choice;
            return Task.CompletedTask;
        }
        public Task RecordConnectionAsync(string coordinatorId, int userId)
        {
            Connections.Add((coordinatorId, userId));
            return Task.CompletedTask;
        }
        public Task<BehaviorSettings> GetSettingsAsync() => Task.FromResult(Settings);
        public Task SaveSettingsAsync(BehaviorSettings settings)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class AccountingStub(IReadOnlyList<SavedCoordinatorModel> servers) : IAccountingService
    {
        public Task<IReadOnlyList<SavedCoordinatorModel>> GetAllSavedCoordinatorsAsync() => Task.FromResult(servers);
        public Task<IReadOnlyList<SavedCoordinatorModel>> GetSavedCoordinatorsAsync(string address, int port) =>
            Task.FromResult<IReadOnlyList<SavedCoordinatorModel>>(servers.Where(server => server.Address == address && server.Port == port).ToArray());
        public Task RemoveUserIdentityAsync(string coordinatorId, int userId) => throw new NotSupportedException();
        public Task SaveCoordinatorInstanceAsync(SavedCoordinatorModel instanceModel, SavedUserModel userModel) => throw new NotSupportedException();
        public Task RemoveCoordinatorInstanceAsync(string coordinatorId) => throw new NotSupportedException();
        public Task ForgetDeviceAsync(string coordinatorId, int userId, LoginLostReason reason) => throw new NotSupportedException();
        public Task KeepRecoveryPhraseAsync(string coordinatorId, int userId, string phrase) => throw new NotSupportedException();
        public Task<string?> GetRecoveryPhraseAsync(string coordinatorId, int userId) => throw new NotSupportedException();
        public Task ConfirmRecoveryPhraseSavedAsync(string coordinatorId, int userId, bool keepOnDevice) => throw new NotSupportedException();
        public bool AreSecretsProtected => true;
        public Task<IReadOnlyList<ServerCertificateModel>> GetTrustedCertificatesAsync() => throw new NotSupportedException();
        public Task RemoveTrustedCertificateAsync(string host, int port) => throw new NotSupportedException();
    }
}
