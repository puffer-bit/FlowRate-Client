using Client.Application.Chat;
using Client.Api.Mappers;
using Client.Core.Coordinator;
using Client.Core.Sessions;
using Client.ViewModels.Sessions.Chat;
using Shared.Enums;
using Shared.Models.Notifications.SessionInfo;
using Xunit;

namespace Client.Application.Tests;

public sealed class ChatTypingTests
{
    [Fact]
    public void ContinuousEditsRenewActivityWithoutSendingEveryKeystroke()
    {
        var typing = new ChatTypingActivity();
        var start = DateTimeOffset.UtcNow;
        Assert.Equal(true, typing.InputChanged(start, true));
        Assert.Null(typing.InputChanged(start.AddSeconds(1), true));
        Assert.Equal(true, typing.InputChanged(start.AddSeconds(2), true));
        Assert.Null(typing.InputChanged(start.AddSeconds(3), true));
        Assert.Null(typing.Expire(start.AddSeconds(7)));
        Assert.Equal(false, typing.Expire(start.AddSeconds(8)));
        Assert.Null(typing.Expire(start.AddSeconds(9)));
        Assert.Equal(true, typing.InputChanged(start.AddSeconds(9), true));
    }

    [Fact]
    public void ClearingOrLeavingStopsImmediatelyAndNextEditStartsAgain()
    {
        var typing = new ChatTypingActivity();
        var now = DateTimeOffset.UtcNow;
        Assert.Null(typing.InputChanged(now, false));
        Assert.Equal(true, typing.InputChanged(now, true));
        Assert.Equal(false, typing.InputChanged(now, false));
        Assert.Equal(true, typing.InputChanged(now, true));
        Assert.Equal(false, typing.Stop());
        Assert.Null(typing.Stop());
        Assert.Equal(true, typing.InputChanged(now, true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NotificationPreservesChatAuthorAndState(bool active)
    {
        var result = CoordinatorEventMapper.ToEvent(new ChatSessionUserTypingNotification(7, 9, active));
        Assert.Equal(new ChatUserTypingEvent(7, 9, active), result);
    }

    [Fact]
    public void StatusReturnsToDescriptionWhenNobodyIsTyping()
    {
        var bar = new ChatStatusBarVM(new ChatSessionModel
        {
            Id = 1, Name = "general", Description = "Team chat", CreatorUserId = 1,
            Capacity = null, SessionType = SessionType.Chat
        });
        bar.SetTypingNames(["Alice", "Bob"]);
        Assert.Contains("Alice", bar.StatusText);
        Assert.Contains("Bob", bar.StatusText);
        bar.SetTypingNames([]);
        Assert.Equal("Team chat", bar.StatusText);
    }
}
