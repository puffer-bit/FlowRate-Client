using System.Reflection;
using System.Windows.Input;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Core.Coordinator;
using Client.Core.Users;
using Client.ViewModels.Sessions.Chat;
using ReactiveUI;
using ReactiveUI.Primitives.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Shared.Enums;
using Shared.Enums.User;
using Xunit;

namespace Client.Application.Tests;

public sealed class ChatViewersTests
{
    private static UserModel User(int id, string name) => new()
        { Id = id, Username = name, CoordinatorInstanceId = "coordinator" };

    [Fact]
    public Task PinnedMessagesAndViewersShareOneSidePanel() => TestUiThread.RunAsync(() =>
    {
        var session = DispatchProxy.Create<ICoordinatorSession, AudienceSession>();
        using var command = ReactiveCommand.Create<int>(_ => { });
        using var bar = new ChatStatusBarVM(new Client.Core.Sessions.ChatSessionModel
        {
            Id = 7, Name = "chat", CreatorUserId = 1, Capacity = null, SessionType = SessionType.Chat
        }) { Viewers = new ChatViewersVM(7, session, command) };
        Assert.False(bar.IsSidePanelOpen);
        bar.IsPinsOpen = true;
        Assert.True(bar.IsSidePanelOpen);
        Assert.False(bar.Viewers.IsOpen);
        var panelStates = new List<bool>();
        bar.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ChatStatusBarVM.IsSidePanelOpen))
                panelStates.Add(bar.IsSidePanelOpen);
        };
        bar.Viewers.IsOpen = true;
        Assert.False(bar.IsPinsOpen);
        Assert.True(bar.IsSidePanelOpen);
        bar.IsPinsOpen = true;
        Assert.False(bar.Viewers.IsOpen);
        Assert.True(bar.IsPinsOpen);
        Assert.NotEmpty(panelStates);
        Assert.All(panelStates, open => Assert.True(open));
        bar.IsPinsOpen = false;
        Assert.False(panelStates[^1]);
        Assert.False(bar.IsSidePanelOpen);
    });

    [Fact]
    public Task DeltasPreserveExistingRowsAndNeverRequestAnotherSnapshot() => TestUiThread.RunAsync(() =>
    {
        var session = DispatchProxy.Create<ICoordinatorSession, AudienceSession>();
        var fake = (AudienceSession)session;
        fake.Initial = [User(1, "Alice"), User(2, "Bob")];
        using var command = ReactiveCommand.Create<int>(_ => { });
        using var vm = new ChatViewersVM(7, session, command);
        vm.IsOpen = true;
        Assert.True(vm.IsOpen);
        Assert.Equal(2, vm.Users.Count);
        var alice = vm.Users[0];
        var id = fake.Requests.Single().Id;
        fake.Raise(new(7, id, [User(1, "Zoe"), User(3, "Carol")], [2]));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "Carol", "Zoe" }, vm.Users.Select(u => u.Username));
        Assert.Same(alice, vm.Users[1]);
        Assert.Single(fake.Requests);
        fake.Raise(new(8, id, [], [1]));
        fake.Raise(new(7, Guid.NewGuid(), [], [1]));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, vm.Users.Count);
        vm.IsOpen = false;
        Assert.False(vm.IsOpen);
        Assert.False(fake.Requests.Last().Subscribe);
        fake.Raise(new(7, id, [User(4, "stale")], []));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(vm.Users);
    });

    [Fact]
    public Task BanAndUnbanRefreshTheExistingRow() => TestUiThread.RunAsync(() =>
    {
        var session = DispatchProxy.Create<ICoordinatorSession, AudienceSession>();
        var fake = (AudienceSession)session;
        fake.Permissions = new CoordinatorPermissions([
            new RoleModel(1, "everyone", null, 0, Permission.ViewSessions, true, 0),
            new RoleModel(2, "moderator", null, 1, Permission.BanMembers, false, 0)
        ], [2]);
        fake.Initial = [new UserModel
            { Id = 3, Username = "Alice", CoordinatorInstanceId = "coordinator", State = UserState.Banned }];
        using var profile = ReactiveCommand.Create<int>(_ => { });
        var banned = new List<int>();
        using var ban = ReactiveCommand.Create<(int UserId, string Name)>(target => banned.Add(target.UserId));
        using var vm = new ChatViewersVM(7, session, profile, banUser: ban, currentUserId: 1);
        vm.Open();
        var row = Assert.Single(vm.Users);
        Assert.True(row.IsBanned);
        Assert.False(row.CanBan);
        row.Ban.Execute().Subscribe();
        Assert.Empty(banned);
        var changes = new List<bool>();
        row.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ChatViewerVM.IsBanned)) changes.Add(row.IsBanned);
        };
        foreach (var state in new[] { UserState.Normal, UserState.Banned })
        {
            fake.Raise(new(7, fake.Requests.Single().Id, [new UserModel
                { Id = 3, Username = "Alice", CoordinatorInstanceId = "coordinator", State = state }], []));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(row, Assert.Single(vm.Users));
            Assert.Equal(state == UserState.Banned, row.IsBanned);
            Assert.Equal(state != UserState.Banned, row.CanBan);
        }
        Assert.Equal(new[] { false, true }, changes);
        Assert.Single(fake.Requests);
    });

    [Fact]
    public async Task OpenProfileDoesNotDisableAudienceRowsWhileTheDialogIsOpen()
    {
        var session = DispatchProxy.Create<ICoordinatorSession, AudienceSession>();
        var fake = (AudienceSession)session;
        fake.Initial = [User(1, "Alice"), User(2, "Bob")];
        var closeProfile = new TaskCompletionSource();
        var opened = new List<int>();
        using var sharedProfile = ReactiveCommand.CreateFromTask<int>(id =>
        {
            opened.Add(id);
            return closeProfile.Task;
        }, outputScheduler: Sequencer.Immediate);
        using var vm = new ChatViewersVM(7, session, sharedProfile);
        await TestUiThread.RunAsync(vm.Open);
        var alice = vm.Users[0];
        var bob = vm.Users[1];
        try
        {
            Task? execution = null;
            await TestUiThread.RunAsync(() => execution = alice.ViewProfile.Execute().ToTask());
            await execution!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await alice.ViewProfile.CanExecute.FirstAsync(enabled => enabled).ToTask()
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(1, Assert.Single(opened));
            Assert.False(closeProfile.Task.IsCompleted);
            Assert.False(((ICommand)sharedProfile).CanExecute(2));
            Assert.True(((ICommand)alice.ViewProfile).CanExecute(null));
            Assert.True(((ICommand)bob.ViewProfile).CanExecute(null));
            Assert.NotSame(alice.ViewProfile, bob.ViewProfile);
            Assert.Single(fake.Requests);
        }
        finally { closeProfile.TrySetResult(); }
    }

    [Fact]
    public async Task DeltaBeforeSnapshotIsAppliedAfterIt()
    {
        var session = DispatchProxy.Create<ICoordinatorSession, AudienceSession>();
        var fake = (AudienceSession)session;
        var pending = new TaskCompletionSource<RequestResult<IReadOnlyList<UserModel>>>();
        fake.Pending = pending.Task;
        using var command = ReactiveCommand.Create<int>(_ => { });
        ChatViewersVM? vm = null;
        await TestUiThread.RunAsync(() =>
        {
            vm = new ChatViewersVM(7, session, command);
            vm.Open();
            fake.Raise(new(7, fake.Requests.Single().Id, [User(2, "Bob")], [1]));
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsLoading);
            pending.SetResult(new RequestResult<IReadOnlyList<UserModel>>([User(1, "Alice")]));
        });
        await TestUiThread.RunAsync(() =>
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, Assert.Single(vm!.Users).Id);
            Assert.Single(fake.Requests);
            vm.Dispose();
        });
    }

    [Fact]
    public Task ContextMenuRespectsSelfAndRoleHierarchyAndUpdatesWithDiffs() => TestUiThread.RunAsync(() =>
    {
        var session = DispatchProxy.Create<ICoordinatorSession, AudienceSession>();
        var fake = (AudienceSession)session;
        fake.Permissions = new CoordinatorPermissions([
            new RoleModel(1, "everyone", null, 0, Permission.ViewSessions, true, 0),
            new RoleModel(2, "moderator", null, 1, Permission.BanMembers, false, 0)
        ], [2]);
        fake.Initial = [User(1, "Self"), User(2, "Alice"), new UserModel
            { Id = 3, Username = "Moderator", CoordinatorInstanceId = "coordinator", RoleIds = [2] }];
        var banned = new List<(int Id, string Name)>();
        using var profile = ReactiveCommand.Create<int>(_ => { });
        using var ban = ReactiveCommand.Create<(int UserId, string Name)>(target => banned.Add(target));
        using var vm = new ChatViewersVM(7, session, profile, banUser: ban, currentUserId: 1);
        vm.Open();
        var alice = vm.Users.Single(row => row.Id == 2);
        Assert.True(alice.CanBan);
        Assert.False(alice.CanMention);
        Assert.False(vm.Users.Single(row => row.Id == 1).CanBan);
        var moderator = vm.Users.Single(row => row.Id == 3);
        Assert.False(moderator.CanBan);
        moderator.Ban.Execute().Subscribe();
        Assert.Empty(banned);
        alice.Ban.Execute().Subscribe();
        Assert.Equal((2, "Alice"), Assert.Single(banned));
        fake.Raise(new(7, fake.Requests.Single().Id, [new UserModel
            { Id = 2, Username = "Alice", CoordinatorInstanceId = "coordinator", RoleIds = [2] }], []));
        Dispatcher.UIThread.RunJobs();
        Assert.False(alice.CanBan);
        Assert.Single(fake.Requests);
    });

    [Fact]
    public Task RoleColorsFollowHighestColoredRoleAndRefreshAfterChanges() => TestUiThread.RunAsync(() =>
    {
        var session = DispatchProxy.Create<ICoordinatorSession, AudienceSession>();
        var fake = (AudienceSession)session;
        fake.Permissions = new CoordinatorPermissions([
            new RoleModel(1, "everyone", null, 0, Permission.ViewSessions, true, 0),
            new RoleModel(2, "member", 0x112233, 1, Permission.None, false, 0),
            new RoleModel(3, "moderator", 0x445566, 2, Permission.None, false, 0),
            new RoleModel(4, "admin", null, 3, Permission.None, false, 0)
        ], []);
        fake.Initial = [new UserModel
            { Id = 1, Username = "Alice", CoordinatorInstanceId = "coordinator", RoleIds = [2, 3, 4] },
            User(2, "Bob")];
        using var profile = ReactiveCommand.Create<int>(_ => { });
        using var vm = new ChatViewersVM(7, session, profile);
        vm.Open();
        var alice = vm.Users.Single(row => row.Id == 1);
        Assert.Equal(0x445566, alice.RoleColor);
        Assert.Null(vm.Users.Single(row => row.Id == 2).RoleColor);
        var changes = new List<int?>();
        alice.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ChatViewerVM.RoleColor)) changes.Add(alice.RoleColor);
        };

        fake.Raise(new(7, fake.Requests.Single().Id, [new UserModel
            { Id = 1, Username = "Alice", CoordinatorInstanceId = "coordinator", RoleIds = [2] }], []));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0x112233, changes[^1]);

        fake.Permissions = new CoordinatorPermissions([
            new RoleModel(2, "member", 0x778899, 1, Permission.None, false, 0)
        ], []);
        fake.RaisePermissionsChanged();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0x778899, changes[^1]);

        fake.Permissions = CoordinatorPermissions.None;
        fake.RaisePermissionsChanged();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(changes[^1]);
        Assert.Same(alice, vm.Users.Single(row => row.Id == 1));
    });

    public class AudienceSession : DispatchProxy
    {
        public CoordinatorPermissions Permissions { get; set; } = CoordinatorPermissions.None;
        public IReadOnlyList<UserModel> Initial { get; set; } = [];
        public Task<RequestResult<IReadOnlyList<UserModel>>>? Pending { get; set; }
        public List<(Guid Id, bool Subscribe)> Requests { get; } = [];
        private Action<ChatViewersChangedEvent>? _changed;
        private Action<CoordinatorPermissions>? _permissionsChanged;
        public void Raise(ChatViewersChangedEvent delta) => _changed?.Invoke(delta);
        public void RaisePermissionsChanged() => _permissionsChanged?.Invoke(Permissions);

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "add_ChatViewersChanged": _changed += (Action<ChatViewersChangedEvent>)args![0]!; return null;
                case "remove_ChatViewersChanged": _changed -= (Action<ChatViewersChangedEvent>)args![0]!; return null;
                case "add_PermissionsChanged": _permissionsChanged += (Action<CoordinatorPermissions>)args![0]!; return null;
                case "remove_PermissionsChanged": _permissionsChanged -= (Action<CoordinatorPermissions>)args![0]!; return null;
                case "get_Permissions": return Permissions;
                case "get_ConnectionStatus": return CoordinatorState.Connected;
                case "SetChatViewersSubscription":
                    Requests.Add(((Guid)args![1]!, (bool)args[2]!));
                    return (bool)args[2]! && Pending != null ? Pending
                        : Task.FromResult(new RequestResult<IReadOnlyList<UserModel>>(Initial));
                default:
                    if (method.Name.StartsWith("add_") || method.Name.StartsWith("remove_")) return null;
                    throw new InvalidOperationException(method.Name);
            }
        }
    }
}
