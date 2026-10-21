using Client.Application.Chat;
using Client.Core.Chat;
using Xunit;

namespace Client.Application.Tests;

public class ChatReadTrackerTests
{
    private const int Me = 1;
    private const int Other = 2;
    private const int ChatId = 7;

    private readonly ChatReadTracker _tracker = new(TimeSpan.Zero);
    private readonly List<(int ChatId, int MessageId)> _sent = [];

    public ChatReadTrackerTests()
    {
        _tracker.Attach(Me, (chatId, messageId) =>
        {
            _sent.Add((chatId, messageId));
            return Task.CompletedTask;
        });
        _tracker.Load([new ChatReadState(ChatId, 10, 0, 0)]);
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
    public void MarkRead_ClearsAtOnce_AndSendsThePosition()
    {
        _tracker.OnMessageAdded(ChatId, Message(11, Other));
        ChatReadState? raised = null;
        _tracker.StateChanged += state => raised = state;

        _tracker.MarkRead(ChatId, 11);

        Assert.Equal(new ChatReadState(ChatId, 11, 0, 0), raised);
        Assert.Equal([(ChatId, 11)], _sent);
    }

    [Fact]
    public void MarkRead_OfWhatIsReadAlready_SendsNothing()
    {
        _tracker.MarkRead(ChatId, 10);
        _tracker.MarkRead(ChatId, 4);

        Assert.Empty(_sent);
    }

    [Fact]
    public void ServerState_BehindThisDevice_IsIgnored()
    {
        _tracker.MarkRead(ChatId, 20);

        _tracker.OnServerState(new ChatReadState(ChatId, 15, 3, 0));
        Assert.Equal(20, _tracker.Get(ChatId)!.LastReadMessageId);

        _tracker.OnServerState(new ChatReadState(ChatId, 25, 0, 0));
        Assert.Equal(25, _tracker.Get(ChatId)!.LastReadMessageId);
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
