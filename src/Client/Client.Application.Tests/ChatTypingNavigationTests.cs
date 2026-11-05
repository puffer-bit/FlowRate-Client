using Client.Application.Chat;
using Client.Application.Coordinator;
using Client.Application.Tests.Fakes;
using Client.Core.Chat;
using Client.Core.Coordinator;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Client.Application.Tests;

public sealed class ChatTypingNavigationTests
{
    [Fact]
    public void SwitchingChatsKeepsOriginalExpiryWithoutRenewingIt()
    {
        var time = new Clock();
        var tracker = new ChatTypingTracker(time);
        tracker.Update(7, 9, true);
        var original = tracker.Get(7)[9];

        time.Now += TimeSpan.FromSeconds(2);
        tracker.Update(8, 10, true);
        Assert.Equal(original, tracker.Get(7)[9]);
        Assert.Single(tracker.Get(8));

        time.Now += TimeSpan.FromSeconds(3);
        Assert.Empty(tracker.Get(7));
        Assert.Single(tracker.Get(8));
    }

    [Fact]
    public async Task ActivityIsKeptWithoutAnOpenChatAndStopsWhenTheAuthorSends()
    {
        var (gateway, fake) = FakeGateway.Create();
        using var session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance);
        await session.Connect("server", 1, null, null);

        fake.Raise(new ChatUserTypingEvent(7, 9, true));
        fake.Raise(new ChatUserTypingEvent(8, 10, true));
        fake.Raise(new ChatUserTypingEvent(7, 1, true));
        Assert.Equal(9, Assert.Single(session.GetChatTypingUsers(7)).Key);
        Assert.Equal(10, Assert.Single(session.GetChatTypingUsers(8)).Key);

        fake.Raise(new ChatMessageEditedEvent(7, new ChatMessageModel
        {
            Id = 1, ChatId = 7, SenderUserId = 9, SentTime = DateTime.UtcNow,
            Images = [], Video = [], Files = [], UsersIds = [],
            Reactions = [new ChatReaction { UserId = 10, Emoji = "👍" }]
        }));
        Assert.Equal(9, Assert.Single(session.GetChatTypingUsers(7)).Key);

        fake.Raise(new ChatMessageAddedEvent(7, new ChatMessageModel
        {
            Id = 1, ChatId = 7, SenderUserId = 9, SentTime = DateTime.UtcNow,
            Images = [], Video = [], Files = [], Reactions = [], UsersIds = []
        }));
        Assert.Empty(session.GetChatTypingUsers(7));
        Assert.Single(session.GetChatTypingUsers(8));

        fake.Raise(new ChatUserTypingEvent(8, 10, false));
        Assert.Empty(session.GetChatTypingUsers(8));
    }

    [Fact]
    public async Task DisconnectClearsActivityBeforeAnotherConnection()
    {
        var (gateway, fake) = FakeGateway.Create();
        using var session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance);
        await session.Connect("server", 1, null, null);
        fake.Raise(new ChatUserTypingEvent(7, 9, true));
        Assert.Single(session.GetChatTypingUsers(7));

        await session.Disconnect();
        Assert.Empty(session.GetChatTypingUsers(7));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
