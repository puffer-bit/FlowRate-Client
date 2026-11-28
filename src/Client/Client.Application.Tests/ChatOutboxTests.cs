using Client.Application.Coordinator;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Client.Core.Users;
using Client.ViewModels.Sessions.Chat;
using Shared.Enums;
using Xunit;

namespace Client.Application.Tests;

public class ChatOutboxTests
{
    private const int ChatId = 7;

    private sealed class FakeSession
    {
        public CoordinatorState State { get; set; } = CoordinatorState.Connected;
        public List<NewChatMessage> Sent { get; } = [];
        public Queue<ErrorCode?> Answers { get; } = new();
        public ICoordinatorSession Session { get; }

        public FakeSession()
        {
            Session = ChatHistoryWindowTests.Proxy<ICoordinatorSession>((name, args) =>
            {
                switch (name)
                {
                    case "GetCurrentUser":
                        return new UserModel { Id = 1, Username = "me", CoordinatorInstanceId = "c" };
                    case "get_ConnectionStatus":
                        return State;
                    case "get_CoordinatorModel":
                        return null;
                    case "SendChatMessage":
                        var message = (NewChatMessage)args![0]!;
                        Sent.Add(message);
                        return Task.FromResult(Answers.TryDequeue(out var error) && error is { } code
                            ? new RequestResult<ChatMessageModel?>(code)
                            : new RequestResult<ChatMessageModel?>(Stored(message)));
                    default:
                        if (name.StartsWith("add_") || name.StartsWith("remove_")) return null;
                        throw new InvalidOperationException(name);
                }
            });
        }

        private static ChatMessageModel Stored(NewChatMessage message) => new()
        {
            Id = 100, ChatId = message.ChatId, SenderUserId = 1, SentTime = DateTimeOffset.UtcNow,
            ClientMessageId = message.ClientMessageId, TextContent = message.TextContent,
            Images = [], Files = [], Reactions = [], IsMine = true
        };
    }

    private static ChatOutgoingMessage Message(string text) =>
        new("c", ChatId, 1, new ChatTextContent { Text = text }, null, null, false, []);

    [Fact]
    public void AMessage_Goes_AndComesBackStored_WithItsClientId()
    {
        var fake = new FakeSession();
        var outbox = new ChatOutbox();
        var sent = new List<(ChatOutgoingMessage Message, ChatMessageModel? Stored)>();
        outbox.Sent += (message, stored) => sent.Add((message, stored));
        var message = Message("hi");

        outbox.Enqueue(fake.Session, message);

        Assert.Equal(message.ClientMessageId, Assert.Single(fake.Sent).ClientMessageId);
        var (gone, stored) = Assert.Single(sent);
        Assert.Same(message, gone);
        Assert.Equal(message.ClientMessageId, stored!.ClientMessageId);
        Assert.Empty(outbox.Get("c", ChatId));
    }

    [Fact]
    public void ALostAnswer_IsSentAgainWithTheSameId_ThenWaitsForTheUser()
    {
        var fake = new FakeSession();
        var outbox = new ChatOutbox();
        for (var i = 0; i < 3; i++)
            fake.Answers.Enqueue(ErrorCode.TimedOut);
        var message = Message("hi");

        outbox.Enqueue(fake.Session, message);

        Assert.Equal(3, fake.Sent.Count);
        Assert.All(fake.Sent, sent => Assert.Equal(message.ClientMessageId, sent.ClientMessageId));
        Assert.True(message.IsFailed);
        Assert.Same(message, Assert.Single(outbox.Get("c", ChatId)));

        outbox.Retry(message);

        Assert.Equal(4, fake.Sent.Count);
        Assert.Empty(outbox.Get("c", ChatId));
    }

    [Fact]
    public void ARefusal_WaitsForTheUser_WhileTheMessagesAfterItGo()
    {
        var fake = new FakeSession();
        var outbox = new ChatOutbox();
        fake.Answers.Enqueue(ErrorCode.AttachmentsNotAllowed);
        var refused = Message("first");
        var next = Message("second");

        outbox.Enqueue(fake.Session, refused);
        outbox.Enqueue(fake.Session, next);

        Assert.True(refused.IsFailed);
        Assert.NotNull(refused.Error);
        Assert.Equal(new[] { "first", "second" }, fake.Sent.Select(sent => sent.TextContent!.Text));
        Assert.Same(refused, Assert.Single(outbox.Get("c", ChatId)));
    }

    [Fact]
    public void WithoutConnection_MessagesWait_AndCanBeTakenBack()
    {
        var fake = new FakeSession { State = CoordinatorState.Disconnected };
        var outbox = new ChatOutbox();
        var removed = new List<ChatOutgoingMessage>();
        outbox.Removed += removed.Add;
        var message = Message("hi");

        outbox.Enqueue(fake.Session, message);

        Assert.Empty(fake.Sent);
        Assert.True(message.IsWaitingForNetwork);

        Assert.True(outbox.TakeBack(message));
        Assert.Same(message, Assert.Single(removed));
        Assert.Empty(outbox.Get("c", ChatId));
    }
}
