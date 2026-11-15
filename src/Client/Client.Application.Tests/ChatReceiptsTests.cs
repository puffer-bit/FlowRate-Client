using Avalonia.Threading;
using Client.Application.Chat;
using Client.Application.Coordinator;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Core.Sessions;
using Client.Core.Users;
using Client.ViewModels.Sessions;
using Client.ViewModels.Sessions.Chat;
using ReactiveUI;
using Shared.Enums;
using Xunit;

namespace Client.Application.Tests;

public sealed class ChatReceiptsTests
{
    private const int ChatId = 7;
    private static readonly DateTime Noon = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A coordinator session that answers the receipt requests and lets the test raise reads.</summary>
    private sealed class Server
    {
        public Action<ChatPeerRead>? PeerRead;
        public int PeerPosition;
        public readonly List<int> Marked = [];
        public TaskCompletionSource<RequestResult<ChatReadState>>? PendingMark;
        public ICoordinatorSession Session { get; }

        public Server(Func<string, object?[]?, object?>? other = null) =>
            Session = ChatHistoryWindowTests.Proxy<ICoordinatorSession>((method, args) =>
            {
                switch (method)
                {
                    case "add_ChatPeerRead":
                        PeerRead += (Action<ChatPeerRead>)args![0]!;
                        return null;
                    case "remove_ChatPeerRead":
                        PeerRead -= (Action<ChatPeerRead>)args![0]!;
                        return null;
                    case nameof(ICoordinatorSession.RequestChatReadStates):
                        return Task.FromResult(new RequestResult<IReadOnlyList<ChatReadState>>(
                            [new ChatReadState(ChatId, 0, 0, 0, PeerPosition)]));
                    case nameof(ICoordinatorSession.MarkChatRead):
                        var position = (int)args![1]!;
                        Marked.Add(position);
                        return PendingMark?.Task ?? Task.FromResult(
                            new RequestResult<ChatReadState>(new ChatReadState(ChatId, position, 0, 0, PeerPosition)));
                    case nameof(ICoordinatorSession.RequestChatMessageReaders):
                        return Task.FromResult(new RequestResult<IReadOnlyList<ChatMessageReader>>(
                            [new ChatMessageReader(2, Noon)]));
                }
                return other != null ? other(method, args) : throw new InvalidOperationException(method);
            });
    }

    [Fact]
    public async Task Attach_LoadsThePositionsOfTheOthers_AndReadsMoveThemForwardOnly()
    {
        var server = new Server { PeerPosition = 5 };
        var receipts = new ChatReceipts();
        var changes = new List<(int, int)>();
        receipts.PeerReadChanged += (chat, position) => changes.Add((chat, position));

        await receipts.AttachAsync(server.Session);
        server.PeerRead!(new ChatPeerRead(ChatId, 2, 9, Noon));
        server.PeerRead!(new ChatPeerRead(ChatId, 3, 4, Noon));

        Assert.Equal(9, receipts.GetPeerRead(ChatId));
        Assert.Equal([(ChatId, 5), (ChatId, 9)], changes);
    }

    [Fact]
    public async Task ReportRead_SendsOneAtATime_WithTheNewestPosition()
    {
        var server = new Server { PendingMark = new() };
        var receipts = new ChatReceipts();
        await receipts.AttachAsync(server.Session);

        receipts.ReportRead(ChatId, 3);
        receipts.ReportRead(ChatId, 4);
        receipts.ReportRead(ChatId, 6);
        receipts.ReportRead(ChatId, 5);
        Assert.Equal([3], server.Marked);

        var first = server.PendingMark;
        server.PendingMark = null;
        first.SetResult(new RequestResult<ChatReadState>(new ChatReadState(ChatId, 3, 0, 0, 0)));
        await WaitUntil(() => server.Marked.Count == 2);

        Assert.Equal([3, 6], server.Marked);
        receipts.ReportRead(ChatId, 6);
        receipts.ReportRead(ChatId, 2);
        Assert.Equal([3, 6], server.Marked);
    }

    [Fact]
    public async Task Detach_ForgetsEverything_AndStopsListening()
    {
        var server = new Server { PeerPosition = 5 };
        var receipts = new ChatReceipts();
        await receipts.AttachAsync(server.Session);

        receipts.Detach();
        receipts.ReportRead(ChatId, 8);

        Assert.Equal(0, receipts.GetPeerRead(ChatId));
        Assert.Null(server.PeerRead);
        Assert.Empty(server.Marked);
    }

    [Fact]
    public Task OwnMessages_TurnRead_WhenTheOthersReadThatFar() => TestUiThread.RunAsync(() =>
    {
        var server = new Server(ChatSession);
        var receipts = new ChatReceipts();
        server.PeerPosition = 1;
        receipts.AttachAsync(server.Session).GetAwaiter().GetResult();
        using var chat = Open(ChatKind.Group, server, receipts, [Message(1, 1), Message(2, 2), Message(3, 1), Message(4, 1)]);

        Assert.Equal([ChatDeliveryStatus.Read, ChatDeliveryStatus.None, ChatDeliveryStatus.Sent, ChatDeliveryStatus.Sent],
            Statuses(chat));

        server.PeerRead!(new ChatPeerRead(ChatId, 2, 3, Noon));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([ChatDeliveryStatus.Read, ChatDeliveryStatus.None, ChatDeliveryStatus.Read, ChatDeliveryStatus.Sent],
            Statuses(chat));
        Assert.True(Find(chat, 3).CanShowReaders);
        Assert.False(Find(chat, 2).CanShowReaders);
    });

    [Fact]
    public Task ServerChannels_ShowNoChecks() => TestUiThread.RunAsync(() =>
    {
        var server = new Server(ChatSession) { PeerPosition = 9 };
        var receipts = new ChatReceipts();
        receipts.AttachAsync(server.Session).GetAwaiter().GetResult();
        using var chat = Open(ChatKind.ServerChannel, server, receipts, [Message(1, 1), Message(2, 2)]);

        Assert.All(Statuses(chat), status => Assert.Equal(ChatDeliveryStatus.None, status));
        Assert.False(Find(chat, 1).CanShowReaders);
    });

    [Fact]
    public Task ReadersList_TakesReadsThatComeWhileItIsOpen() => TestUiThread.RunAsync(() =>
    {
        var server = new Server(ChatSession);
        var receipts = new ChatReceipts();
        receipts.AttachAsync(server.Session).GetAwaiter().GetResult();
        using var chat = Open(ChatKind.Direct, server, receipts, [Message(1, 1)]);
        var message = Find(chat, 1);

        var readers = message.Readers!;
        Pump(() => !readers.IsLoading);
        Assert.Equal([2], readers.Items.Select(item => item.UserId));

        server.PeerRead!(new ChatPeerRead(ChatId, 3, 1, Noon.AddMinutes(1)));
        Pump(() => readers.Items.Count == 2);

        Assert.Equal([2, 3], readers.Items.Select(item => item.UserId));
        Assert.Equal(Noon, readers.FirstReadAt);
        Assert.Contains(ChatReceiptTime.Format(Noon), message.DeliveryTooltip);
    });

    private static ChatMessageModel Message(int id, int senderId) => new()
    {
        Id = id, ChatId = ChatId, SenderUserId = senderId, SentTime = Noon, IsMine = senderId == 1,
        Images = [], Video = [], Files = [], Reactions = [], UsersIds = []
    };

    private static object? ChatSession(string method, object?[]? args) => method switch
    {
        nameof(ICoordinatorSession.GetCurrentUser) => new UserModel { Id = 1, Username = "Me", CoordinatorInstanceId = "server" },
        nameof(ICoordinatorSession.GetUser) => Task.FromResult<UserModel?>(new UserModel
            { Id = (int)args![0]!, Username = $"User {args[0]}", CoordinatorInstanceId = "server" }),
        nameof(ICoordinatorSession.GetUsers) => Task.FromResult<IReadOnlyDictionary<int, UserModel>>(
            ((IEnumerable<int>)args![0]!).ToDictionary(id => id,
                id => new UserModel { Id = id, Username = $"User {id}", CoordinatorInstanceId = "server" })),
        nameof(ICoordinatorSession.IsUserDeleted) => false,
        nameof(ICoordinatorSession.GetChatTypingUsers) => new Dictionary<int, DateTimeOffset>(),
        "get_Permissions" => new CoordinatorPermissions([], [], 1),
        "get_ConnectionStatus" => CoordinatorState.Disconnected,
        "get_DirectCall" => null,
        _ when method.StartsWith("add_") || method.StartsWith("remove_") => null,
        _ => throw new InvalidOperationException(method)
    };

    private static ActiveChatSessionVM Open(ChatKind kind, Server server, ChatReceipts receipts,
        IReadOnlyList<ChatMessageModel> messages)
    {
        var cache = ChatHistoryWindowTests.Proxy<IChatCache>((method, _) => method == nameof(IChatCache.LoadNewestAsync)
            ? Task.FromResult<Ports.Repositories.CachedChatPage?>(new(messages, 1, true))
            : null);
        var caches = ChatHistoryWindowTests.Proxy<IChatCacheStore>((_, _) => cache);
        var media = ChatHistoryWindowTests.Proxy<Ports.IMediaCache>((_, _) => null);
        var users = ReactiveCommand.Create<int>(_ => { });
        var chat = new ActiveChatSessionVM(new ChatSessionModel
            {
                Id = ChatId, Name = "Chat", Capacity = -1, SessionType = SessionType.Chat,
                CreatorUserId = 1, AdminUserId = 1, Kind = kind,
                MemberIds = kind == ChatKind.ServerChannel ? [] : [1, 2, 3]
            }, server.Session,
            new ChatSessionServices(caches, null!, null!, null!, media, Receipts: receipts),
            new ChatReadTracker(), users);
        var load = chat.EnsureLoadedAsync();
        Pump(() => load.IsCompleted);
        return chat;
    }

    private static void Pump(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WaitUntil(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private static ChatMessageVM Find(ActiveChatSessionVM chat, int id) =>
        chat.Messages.OfType<ChatMessageVM>().Single(message => message.Message.Id == id);

    private static List<ChatDeliveryStatus> Statuses(ActiveChatSessionVM chat) =>
        chat.Messages.OfType<ChatMessageVM>().OrderBy(message => message.Message.Id)
            .Select(message => message.DeliveryStatus).ToList();
}
