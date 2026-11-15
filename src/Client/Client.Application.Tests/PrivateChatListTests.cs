using System.Reflection;
using System.Reactive.Disposables;
using Client.Application.Chat;
using Client.Application.Coordinator.Users;
using Client.Application.Sessions.Hybrid;
using Client.Core.Chat;
using Client.Core.Sessions;
using Client.ViewModels.Main;
using Client.ViewModels.Sessions.Hybrid;
using Client.ViewModels.Sessions.List;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;
using Shared.Enums;
using Xunit;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.Application.Tests;

public sealed class PrivateChatListTests
{
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
        Id = id, Name = $"Chat {id}", Kind = kind, SessionType = SessionType.Chat, Capacity = null,
        CreatorUserId = 1,
        LastMessage = new ChatMessageModel
        {
            Id = id, ChatId = id, SenderUserId = 1, SentTime = new DateTime(2026, 11, messageDay),
            Images = [], Video = [], Files = [], Reactions = [], UsersIds = []
        }
    };

    private sealed class ListFixture : IDisposable
    {
        private readonly CompositeDisposable _commands = new();
        public SessionsListVM List { get; }

        public ListFixture() => List = new SessionsListVM(
            Command<RxVoid>(), Command<SessionModel>(), Command<int>(), Command<int>(), Command<SessionModel>(),
            Command<SessionModel>(), Command<int>(), Command<(int, int)>(), Command<(int, string)>(),
            Command<(int, int, bool)>(), Command<(int, int, string?)>(), Command<int?>(),
            Proxy<IPeerPlaybackService>(), Proxy<IVoiceActivityService>(), Proxy<IScreenWatchService>(),
            new PeerStatsService(), null!, new ChatReadTracker(), new AccessVM(), NullLogger<SessionsListVM>.Instance);

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
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }
}
