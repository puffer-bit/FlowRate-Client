using System.Reactive.Linq;
using Client.Application.Coordinator;
using Client.Application.Tests.Fakes;
using Client.Core.Coordinator;
using Client.Services;
using Client.ViewModels.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models.DataTransferObjects;
using Xunit;

namespace Client.Application.Tests;

public sealed class ChatCallTests
{
    private static ChatCallDTO Call(Guid id, ChatCallState state, int chatId = 7, bool selected = true,
        params ChatCallParticipantDTO[] participants) => new(id, chatId, 1, state,
        state == ChatCallState.Ended ? ChatCallEndReason.Completed : null, DateTimeOffset.UtcNow, null,
        participants.Length > 0 ? participants : [new ChatCallParticipantDTO(1, ChatCallParticipantState.Joined)],
        selected, null);

    [Fact]
    public async Task LateResponseCannotRollBackWhatNotificationsSaid()
    {
        var (gateway, fake) = FakeGateway.Create();
        using var session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance);
        await session.Connect("server", 1, null, new RegisterLoginPlan());
        var id = Guid.NewGuid();
        var started = Call(id, ChatCallState.Active);
        var joined = started with { Participants = [new(1, ChatCallParticipantState.Joined), new(2, ChatCallParticipantState.Joined)] };
        var response = new TaskCompletionSource<ChatCallDTO>();
        fake.CallHandler = (_, _, _) => response.Task;
        var seen = new List<ChatCallDTO>();
        session.ChatCallChanged += seen.Add;
        var request = session.ChangeChatCall(7, id, ChatCallAction.Start);
        fake.Raise(new ChatCallChangedEvent(started));
        fake.Raise(new ChatCallChangedEvent(joined));
        response.SetResult(started);
        await request;
        Assert.Same(joined, session.ChatCalls[7]);
        Assert.Same(joined, session.CurrentCall);
        Assert.Equal(new[] { started, joined }, seen);
    }

    [Fact]
    public async Task EndedCallStaysEnded()
    {
        var (gateway, fake) = FakeGateway.Create();
        using var session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance);
        await session.Connect("server", 1, null, new RegisterLoginPlan());
        var id = Guid.NewGuid();
        var active = Call(id, ChatCallState.Active);
        fake.Raise(new ChatCallChangedEvent(active));
        fake.Raise(new ChatCallChangedEvent(active with { State = ChatCallState.Ended }));
        fake.Raise(new ChatCallChangedEvent(active));
        Assert.Empty(session.ChatCalls);
        Assert.Null(session.CurrentCall);
    }

    [Fact]
    public async Task CallsOfOtherChatsAreKnownButNotCurrent()
    {
        var (gateway, fake) = FakeGateway.Create();
        using var session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance);
        await session.Connect("server", 1, null, new RegisterLoginPlan());
        var mine = Call(Guid.NewGuid(), ChatCallState.Active);
        var other = Call(Guid.NewGuid(), ChatCallState.Active, chatId: 8, selected: false);
        fake.Raise(new ChatCallChangedEvent(mine));
        fake.Raise(new ChatCallChangedEvent(other));
        Assert.Equal(2, session.ChatCalls.Count);
        Assert.Same(mine, session.CurrentCall);
    }

    [Fact]
    public async Task ResponseCountsForACallNoNotificationToldAbout()
    {
        var (gateway, fake) = FakeGateway.Create();
        using var session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance);
        await session.Connect("server", 1, null, new RegisterLoginPlan());
        var call = Call(Guid.NewGuid(), ChatCallState.Active);
        fake.CallHandler = (_, _, _) => Task.FromResult(call);
        await session.ChangeChatCall(7, call.CallId, ChatCallAction.Start);
        Assert.Same(call, session.CurrentCall);
    }

    [Fact]
    public async Task DisconnectClearsCallsAndIgnoresLateResponse()
    {
        var (gateway, fake) = FakeGateway.Create();
        using var session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance);
        await session.Connect("server", 1, null, new RegisterLoginPlan());
        var call = Call(Guid.NewGuid(), ChatCallState.Active);
        fake.Raise(new ChatCallChangedEvent(call));
        Assert.NotNull(session.CurrentCall);
        var response = new TaskCompletionSource<ChatCallDTO>();
        fake.CallHandler = (_, _, _) => response.Task;
        var request = session.ChangeChatCall(7, call.CallId, ChatCallAction.Leave);
        await session.Disconnect();
        response.SetResult(call);
        await request;
        Assert.Null(session.CurrentCall);
        Assert.Empty(session.ChatCalls);
    }

    [Fact]
    public Task InvitationButtonsCannotRaceOrRunAfterDismissal() => TestUiThread.RunAsync(() =>
    {
        var service = new NotificationService();
        var pending = new TaskCompletionSource();
        var accepts = 0;
        var declines = 0;
        var toast = service.ShowActions("Incoming call", "Alice", "Accept", () =>
        { accepts++; return pending.Task; }, "Decline", () => { declines++; return Task.CompletedTask; });
        using var accept = toast.PrimaryCommand!.Execute().Subscribe();
        using var decline = toast.SecondaryCommand!.Execute().Subscribe();
        Assert.Equal(1, accepts);
        Assert.Equal(0, declines);
        Assert.False(toast.AreActionsEnabled);
        _ = toast.CloseAsync();
        pending.SetResult();
        using var afterClose = toast.SecondaryCommand.Execute().Subscribe();
        Assert.Equal(0, declines);
        Assert.True(toast.IsLeaving);
        Assert.Same(toast, Assert.Single(service.History));
    });
}
