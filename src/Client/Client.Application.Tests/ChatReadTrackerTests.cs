using Client.Application.Chat;
using Client.Core.Chat;
using Xunit;

namespace Client.Application.Tests;

public class ChatReadTrackerTests
{
    private const int Me = 1;
    private const int Other = 2;
    private const int ChatId = 7;

    private readonly ChatReadTracker _tracker = new();

    public ChatReadTrackerTests()
    {
        _tracker.AttachAsync("server", Me).GetAwaiter().GetResult();
        _tracker.MarkRead(ChatId, 10);
    }

    private static ChatMessageModel Message(int id, int senderId, bool mentionsMe = false, bool here = false,
        bool system = false) => new()
    {
        Id = id,
        ChatId = ChatId,
        SenderUserId = senderId,
        SentTime = DateTime.UtcNow,
        Images = [],
        Video = [],
        Files = [],
        Reactions = [],
        UsersIds = [],
        MentionedUserIds = mentionsMe ? [Me] : [],
        MentionsHere = here,
        IsSystem = system
    };

    [Fact]
    public void MessagesOfOthers_AddToTheCounts_MentionsSeparately()
    {
        _tracker.OnMessageAdded(ChatId, Message(11, Other));
        _tracker.OnMessageAdded(ChatId, Message(12, Other, mentionsMe: true));
        _tracker.OnMessageAdded(ChatId, Message(13, Other, here: true));
        _tracker.OnMessageAdded(ChatId, Message(14, Other, system: true));
        _tracker.OnMessageAdded(ChatId, Message(9, Other));

        Assert.Equal(new ChatReadState(ChatId, 10, 3, 1), _tracker.Get(ChatId));
    }

    [Fact]
    public void ChatBeingRead_DoesNotCount()
    {
        _tracker.SetReading(ChatId, true);
        _tracker.OnMessageAdded(ChatId, Message(11, Other));

        _tracker.SetReading(ChatId, false);
        _tracker.OnMessageAdded(ChatId, Message(12, Other));

        Assert.Equal(1, _tracker.Get(ChatId)!.UnreadCount);
    }

    [Fact]
    public void OwnMessage_ReadsEverythingBeforeIt()
    {
        _tracker.OnMessageAdded(ChatId, Message(11, Other, mentionsMe: true));
        _tracker.OnMessageAdded(ChatId, Message(12, Me));

        Assert.Equal(new ChatReadState(ChatId, 12, 0, 0), _tracker.Get(ChatId));
    }

    [Fact]
    public void MarkRead_ClearsAtOnce()
    {
        _tracker.OnMessageAdded(ChatId, Message(11, Other));
        ChatReadState? raised = null;
        _tracker.StateChanged += state => raised = state;

        _tracker.MarkRead(ChatId, 11);

        Assert.Equal(new ChatReadState(ChatId, 11, 0, 0), raised);
    }

    [Fact]
    public void MarkRead_OfWhatIsReadAlready_DoesNotGoBack()
    {
        _tracker.MarkRead(ChatId, 10);
        _tracker.MarkRead(ChatId, 4);

        Assert.Equal(10, _tracker.Get(ChatId)!.LastReadMessageId);
    }

    [Fact]
    public void HistoryAndLiveDelivery_CountEachMessageOnce()
    {
        _tracker.OnMessageAdded(ChatId, Message(13, Other, mentionsMe: true));
        _tracker.OnHistoryMessage(ChatId, Message(11, Other));
        _tracker.OnHistoryMessage(ChatId, Message(12, Me));
        _tracker.OnHistoryMessage(ChatId, Message(13, Other, mentionsMe: true));

        Assert.Equal(new ChatReadState(ChatId, 12, 1, 1), _tracker.Get(ChatId));
    }

    [Fact]
    public async Task ReadPositionSurvivesRestartAndIsIsolatedByServerAndAccount()
    {
        var repository = new MemoryReads();
        var tracker = new ChatReadTracker(repository);
        await tracker.AttachAsync("server", Me);
        tracker.MarkRead(ChatId, 20);
        tracker.Detach();
        tracker = new ChatReadTracker(repository);
        await tracker.AttachAsync("server", Me);
        tracker.OnHistoryMessage(ChatId, Message(19, Other));
        tracker.OnHistoryMessage(ChatId, Message(21, Other, mentionsMe: true));
        Assert.Equal(new ChatReadState(ChatId, 20, 1, 1), tracker.Get(ChatId));

        await tracker.AttachAsync("other-server", Me);
        Assert.Null(tracker.Get(ChatId));
        await tracker.AttachAsync("server", Other);
        Assert.Null(tracker.Get(ChatId));
    }

    private sealed class MemoryReads : Client.Application.Ports.Repositories.IChatReadStateRepository
    {
        private readonly Dictionary<(string, int, int), ChatReadState> _states = new();
        public Task<IReadOnlyList<ChatReadState>> GetAsync(string coordinatorInstanceId, int userId) =>
            Task.FromResult<IReadOnlyList<ChatReadState>>(_states.Where(pair =>
                pair.Key.Item1 == coordinatorInstanceId && pair.Key.Item2 == userId)
                .Select(pair => pair.Value).ToArray());
        public Task SetAsync(string coordinatorInstanceId, int userId, ChatReadState state)
        {
            _states[(coordinatorInstanceId, userId, state.ChatId)] = state;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void ChatCreatedAfterLoading_CountsItsMessages()
    {
        _tracker.OnMessageAdded(ChatId + 1, Message(3, Other, mentionsMe: true));

        Assert.Equal(new ChatReadState(ChatId + 1, 0, 1, 1), _tracker.Get(ChatId + 1));
    }

    [Fact]
    public void Detach_ForgetsTheCounts()
    {
        _tracker.Detach();

        Assert.Null(_tracker.Get(ChatId));
    }
}
