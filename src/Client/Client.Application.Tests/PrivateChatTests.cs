using System.Reflection;
using System.Reactive.Linq;
using Client.Application.Coordinator;
using Client.Core.Coordinator;
using Client.Core.Sessions;
using Client.Core.Users;
using Client.ViewModels.Sessions.Create;
using Client.ViewModels.Sessions.Chat;
using Avalonia.Threading;
using ReactiveUI;
using Shared.Enums;
using Xunit;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace Client.Application.Tests;

public sealed class PrivateChatTests
{
    [Theory]
    [InlineData(ChatKind.Direct)]
    [InlineData(ChatKind.Group)]
    public void PrivatePermissions_IgnoreServerAdministrator_AndRequireMembership(ChatKind kind)
    {
        var chat = Model(kind, 1, 2);
        var roles = new[] { new RoleModel(1, "@everyone", null, 0, Permission.Administrator, true, 0) };
        Assert.False(new CoordinatorPermissions(roles, [], 99).HasIn(chat, Permission.ViewSessions));
        var member = new CoordinatorPermissions([], [], 2);
        Assert.True(member.HasIn(chat, Permission.ViewSessions | Permission.SendMessages | Permission.AttachFiles));
        Assert.False(member.HasIn(chat, Permission.ManageSessions));
        Assert.Equal(kind == ChatKind.Group, new CoordinatorPermissions([], [], 1).HasIn(chat, Permission.ManageSessions));
    }

    [Fact]
    public Task PersonalCreation_UsesTheSelectedUser_AndReturnsTheConversationToOpen() => TestUiThread.RunAsync(() =>
    {
        var session = DispatchProxy.Create<ICoordinatorSession, Session>();
        var proxy = (Session)(object)session;
        using var form = new CreateSessionWindowVM(session, privateMode: true);
        using var close = form.CloseWindow.RegisterHandler(context => context.SetOutput(RxVoid.Default));
        form.ToggleMemberCommand.Execute(new ChatMemberChoiceVM(2, "Peer")).Wait();
        form.CreateCommand.Execute().Wait();
        Assert.Equal(ChatKind.Direct, ((NewChatSessionOptions)proxy.Created!.Options).Kind);
        Assert.Equal(new[] { 2 }, ((NewChatSessionOptions)proxy.Created.Options).MemberIds);
        Assert.NotNull(form.CreatedSession);
        Assert.False(form.ShowCapacity);
        Assert.False(form.ShowName);
    });

    [Fact]
    public Task GroupCreation_UsesTheSameForm_WithNameAndMembers() => TestUiThread.RunAsync(() =>
    {
        var session = DispatchProxy.Create<ICoordinatorSession, Session>();
        var proxy = (Session)(object)session;
        using var form = new CreateSessionWindowVM(session, privateMode: true) { IsGroup = true, Name = "Friends" };
        using var close = form.CloseWindow.RegisterHandler(context => context.SetOutput(RxVoid.Default));
        form.ToggleMemberCommand.Execute(new ChatMemberChoiceVM(2, "Peer")).Wait();
        form.ToggleMemberCommand.Execute(new ChatMemberChoiceVM(3, "Other")).Wait();
        form.CreateCommand.Execute().Wait();
        Assert.Equal("Friends", proxy.Created!.Name);
        Assert.Equal(ChatKind.Group, ((NewChatSessionOptions)proxy.Created.Options).Kind);
        Assert.Equal(new[] { 2, 3 }, ((NewChatSessionOptions)proxy.Created.Options).MemberIds);
        Assert.True(form.ShowName);
    });

    [Fact]
    public Task GroupMemberSearchUsesAValidProtocolLimit() => TestUiThread.RunAsync(() =>
    {
        var session = DispatchProxy.Create<ICoordinatorSession, Session>();
        var proxy = (Session)(object)session;
        using var form = new CreateSessionWindowVM(session, privateMode: true) { IsGroup = true };
        form.InitializeMembersAsync().GetAwaiter().GetResult();
        Assert.InRange(proxy.SearchLimit, 1, 20);
        Assert.Equal("Peer", Assert.Single(form.SearchResults).Name);
        form.SelectDirectCommand.Execute().Wait();
        Assert.False(form.IsGroup);
        form.SelectGroupCommand.Execute().Wait();
        Assert.True(form.IsGroup);
    });

    [Fact]
    public Task DirectChatHeaderUsesPeerProfileAndNeverSubscribesToViewers() => TestUiThread.RunAsync(() =>
    {
        var session = ChatHistoryWindowTests.Proxy<ICoordinatorSession>((method, args) => method switch
        {
            nameof(ICoordinatorSession.GetCurrentUser) => new UserModel { Id = 1, Username = "Me", CoordinatorInstanceId = "server" },
            nameof(ICoordinatorSession.GetUser) => Task.FromResult<UserModel?>(new UserModel { Id = 2, Username = "Peer", AvatarUrl = "/avatar/2", CoordinatorInstanceId = "server" }),
            nameof(ICoordinatorSession.GetChatTypingUsers) => new Dictionary<int, DateTimeOffset>(),
            "get_Permissions" => new CoordinatorPermissions([], [], 1),
            "get_ConnectionStatus" => CoordinatorState.Connected,
            _ when method.StartsWith("add_") || method.StartsWith("remove_") => null,
            _ => throw new InvalidOperationException(method)
        });
        var cache = ChatHistoryWindowTests.Proxy<Client.Application.Chat.IChatCache>((_, _) => null);
        var caches = ChatHistoryWindowTests.Proxy<Client.Application.Chat.IChatCacheStore>((_, _) => cache);
        var media = ChatHistoryWindowTests.Proxy<Client.Application.Ports.IMediaCache>((_, _) => null);
        var profiles = new List<int>();
        using var users = ReactiveCommand.Create<int>(profiles.Add);
        using var chat = new ActiveChatSessionVM(Model(ChatKind.Direct, 1, 2), session,
            new Client.ViewModels.Sessions.ChatSessionServices(caches, null!, null!, null!, media),
            new Client.Application.Chat.ChatReadTracker(), users);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(chat.StatusBar.Viewers);
        Assert.False(chat.StatusBar.HasViewers);
        Assert.Equal("Peer", chat.StatusBar.Name);
        Assert.Equal("/avatar/2", chat.StatusBar.AvatarUrl);
        chat.StatusBar.OpenInfoCommand.Execute().Wait();
        Assert.Equal(new[] { 2 }, profiles);
    });

    private static ChatSessionModel Model(ChatKind kind, params int[] members) => new()
    {
        Id = 7, Name = "Chat", Capacity = -1, SessionType = SessionType.Chat,
        CreatorUserId = 1, AdminUserId = 1, Kind = kind, MemberIds = members
    };

    public class Session : DispatchProxy
    {
        public int SearchLimit { get; private set; }
        public NewSessionModel? Created { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            nameof(ICoordinatorSession.GetCurrentUser) => new UserModel { Id = 1, Username = "Me", CoordinatorInstanceId = "server" },
            nameof(ICoordinatorSession.SearchUsers) => Search((int)args![1]!),
            nameof(ICoordinatorSession.CreateConversation) => Create((NewSessionModel)args![0]!),
            _ => throw new NotSupportedException(method.Name)
        };
        private Task<IReadOnlyList<UserModel>> Search(int limit)
        {
            SearchLimit = limit;
            return Task.FromResult<IReadOnlyList<UserModel>>([new UserModel { Id = 2, Username = "Peer", CoordinatorInstanceId = "server" }]);
        }
        private Task<RequestResult<SessionModel>> Create(NewSessionModel session)
        {
            Created = session;
            return Task.FromResult(new RequestResult<SessionModel>(Model(((NewChatSessionOptions)session.Options).Kind, 1, 2)));
        }
    }
}
