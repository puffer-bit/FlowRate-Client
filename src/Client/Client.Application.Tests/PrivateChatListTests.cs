using System.Reflection;
using System.Reactive.Disposables;
using Avalonia.Threading;
using Client.Application.Chat;
using Client.Application.Coordinator;
using Client.Application.Coordinator.Users;
using Client.Application.Sessions.Hybrid;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Core.Sessions;
using Client.Core.Sessions.Hybrid;
using Client.Core.Users;
using Client.ViewModels.Main;
using Client.ViewModels.Sessions.Hybrid;
using Client.ViewModels.Sessions.List;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;
using Shared.Enums;
using Shared.Enums.WebRTC;
using Xunit;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.Application.Tests;

public sealed class PrivateChatListTests
{
    private static CoordinatorPermissions Permissions(Permission granted) => new(
        [new RoleModel(1, "@everyone", null, 0, granted, true, 0)], []);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task VisibleAgainHybridChannelLoadsExistingParticipants(bool failed) => TestUiThread.RunAsync(() =>
    {
        using var fixture = new ListFixture();
        var list = fixture.List;
        list.Access.Update(Permissions(Permission.None));
        list.Access.Update(Permissions(Permission.Default));
        var user = new UserModel { Id = 2, CoordinatorInstanceId = "server", Username = "Existing peer" };
        var requested = new List<int>();
        var coordinator = DispatchProxy.Create<ICoordinatorSession, ServiceProxy>();
        ((ServiceProxy)(object)coordinator).Handler = (method, args) => method!.Name switch
        {
            nameof(ICoordinatorSession.GetCurrentUser) => user,
            nameof(ICoordinatorSession.GetAllHybridSessionPeers) => Peers((int)args![0]!),
            nameof(ICoordinatorSession.GetUsers) => Task.FromResult<IReadOnlyDictionary<int, UserModel>>(
                new Dictionary<int, UserModel> { [2] = user }),
            _ => throw new InvalidOperationException(method.Name)
        };
        Task<RequestResult<Dictionary<int, PeerModel>>> Peers(int sessionId)
        {
            requested.Add(sessionId);
            RequestResult<Dictionary<int, PeerModel>> result = failed ? ErrorCode.Internal
                : new Dictionary<int, PeerModel>
                {
                    [2] = new() { UserId = 2, State = HybridSessionPeerState.Connected, IsMicrophoneMuted = true }
                };
            return Task.FromResult(result);
        }
        typeof(SessionsListVM).GetField("_coordinatorSession", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(list, coordinator);
        var model = new HybridSessionModel
        {
            Id = 10, Name = "Restored channel", SessionType = SessionType.Hybrid, Capacity = 10,
            EngineType = WebRTCEngine.Mediasoup
        };

        typeof(SessionsListVM).GetMethod("OnHybridSessionAdded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(list, [model]);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([10], requested);
        var channel = Assert.IsType<HybridSessionVM>(Assert.Single(list.Sessions));
        Assert.Equal(failed, channel.IsCorrupted);
        if (failed)
            Assert.Empty(channel.Peers);
        else
        {
            var peer = Assert.Single(channel.Peers);
            Assert.Equal(2, peer.PeerData.UserId);
            Assert.True(peer.PeerData.IsMicrophoneMuted);
            Assert.Equal("Existing peer", peer.UserData!.Username);
        }
    });

    [Fact]
    public Task RevokingChannelAccessForcesPrivateModeAndPreventsSwitchingBack() => TestUiThread.RunAsync(() =>
    {
        using var fixture = new ListFixture();
        var list = fixture.List;
        list.IsPrivateMode = false;
        list.Access.Update(Permissions(Permission.None));

        Assert.False(list.Access.CanViewServerChannels);
        Assert.True(list.IsPrivateMode);
        Assert.False(list.CanShowChannels);
        list.ToggleListMode();
        Assert.True(list.IsPrivateMode);
        list.IsPrivateMode = false;
        Assert.True(list.IsPrivateMode);

        list.Access.Update(Permissions(Permission.ViewSessions));
        Assert.True(list.CanShowChannels);
        list.ToggleListMode();
        Assert.False(list.IsPrivateMode);
    });

    [Theory]
    [InlineData(Permission.ViewSessions)]
    [InlineData(Permission.Administrator)]
    public Task EmptyChannelsStillAllowSwitchingWhenPermissionExists(Permission granted) => TestUiThread.RunAsync(() =>
    {
        using var fixture = new ListFixture();
        var list = fixture.List;
        list.Access.Update(Permissions(granted));
        list.IsPrivateMode = true;

        Assert.Empty(list.Sessions);
        Assert.True(list.Access.CanViewServerChannels);
        Assert.True(list.CanShowChannels);
        list.ToggleListMode();
        Assert.False(list.IsPrivateMode);
    });

    [Fact]
    public Task HomeListKeepsPersonalChatsInRecentOrderInEitherSidebarMode() => TestUiThread.RunAsync(() =>
    {
        using var fixture = new ListFixture();
        var list = fixture.List;
        using var channel = fixture.Chat(1, ChatKind.ServerChannel, 5);
        using var direct = fixture.Chat(2, ChatKind.Direct, 1);
        using var group = fixture.Chat(3, ChatKind.Group, 2);
        list.Sessions.Add(channel);
        list.Sessions.Add(direct);
        list.Sessions.Add(group);

        list.IsPrivateMode = true;
        Assert.Equal([3, 2], list.PrivateChats.Select(chat => chat.Model.Id));
        list.IsPrivateMode = false;
        Assert.Same(channel, Assert.Single(list.Rows));
        Assert.Equal([3, 2], list.PrivateChats.Select(chat => chat.Model.Id));

        direct.Model = Model(2, ChatKind.Direct, 3);
        list.IsPrivateMode = true;
        Assert.Equal([2, 3], list.PrivateChats.Select(chat => chat.Model.Id));

        list.DetachCoordinator();
        Assert.Empty(list.PrivateChats);
        Assert.False(list.HasPrivateChats);
    });

    [Fact]
    public Task ReconnectKeepsHomeChatsWhenTheServerChannelListIsEmpty() => TestUiThread.RunAsync(() =>
    {
        using var fixture = new ListFixture();
        using var direct = fixture.Chat(2, ChatKind.Direct, 1);
        var list = fixture.List;
        list.Sessions.Add(direct);
        list.IsPrivateMode = true;
        list.IsPrivateMode = false;
        Assert.Empty(list.Rows);
        typeof(SessionsListVM).GetProperty(nameof(SessionsListVM.State))!.SetValue(list, SessionsListState.Loaded);
        Assert.True(list.HasPrivateChats);

        list.ShowReconnecting();
        Assert.True(list.IsStale);
        Assert.True(list.HasPrivateChats);
        Assert.Same(direct, Assert.Single(list.PrivateChats));
        Assert.False(list.IsPrivateChatsEmpty);
    });

    private static ChatSessionModel Model(int id, ChatKind kind, int messageDay) => new()
    {
        Id = id, Name = $"Chat {id}", Kind = kind, SessionType = SessionType.Chat, CreatorUserId = 1,
        LastMessage = new ChatMessageModel
        {
            Id = id, ChatId = id, SenderUserId = 1, SentTime = new DateTimeOffset(2026, 11, messageDay, 0, 0, 0, TimeSpan.Zero),
            Images = [], Files = [], Reactions = []
        }
    };

    private sealed class ListFixture : IDisposable
    {
        private readonly CompositeDisposable _commands = new();
        public SessionsListVM List { get; }

        public ListFixture()
        {
            var access = new AccessVM();
            access.Update(Permissions(Permission.ViewSessions));
            List = new SessionsListVM(
            Command<RxVoid>(), Command<SessionModel>(), Command<int>(), Command<int>(), Command<SessionModel>(),
            Command<SessionModel>(), Command<int>(), Command<(int, int)>(), Command<(int, string)>(),
            Command<(int, int, bool)>(), Command<(int, int, string?)>(), Command<int?>(),
            Proxy<IPeerPlaybackService>(), Proxy<IVoiceActivityService>(), Proxy<IScreenWatchService>(),
            new PeerStatsService(), null!, new ChatReadTracker(), access, NullLogger<SessionsListVM>.Instance);
        }

        private static T Proxy<T>() where T : class => DispatchProxy.Create<T, ServiceProxy>();

        private ReactiveCommand<T, RxVoid> Command<T>()
        {
            var command = ReactiveCommand.Create<T>(_ => { });
            _commands.Add(command);
            return command;
        }

        public ChatSessionVM Chat(int id, ChatKind kind, int messageDay) => new(Model(id, kind, messageDay),
            Command<SessionModel>(), Command<int>(), Command<int>(), Command<SessionModel>(), Command<int>());

        public void Dispose() => _commands.Dispose();
    }

    public class ServiceProxy : DispatchProxy
    {
        public Func<MethodInfo?, object?[]?, object?>? Handler { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (Handler != null) return Handler(targetMethod, args);
            var type = targetMethod!.ReturnType;
            if (type == typeof(void)) return null;
            if (type == typeof(Task)) return Task.CompletedTask;
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}
