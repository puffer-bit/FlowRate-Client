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

public sealed class DirectCallTests
{
    private static DirectCallDTO Call(Guid id, DirectCallState state) => new(id, 7, 1, 2, state,
        state == DirectCallState.Ended ? DirectCallEndReason.Completed : null,
        DateTimeOffset.UtcNow, null, true, null);

    [Fact]
    public async Task LateResponseCannotRollBackAcceptedOrEndedCall()
    {
        var (gateway, fake) = FakeGateway.Create();
        using var session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance);
        await session.Connect("server", 1, null, null);
        var id = Guid.NewGuid();
        var ringing = Call(id, DirectCallState.Ringing);
        var active = ringing with { State = DirectCallState.Active };
        var ended = active with { State = DirectCallState.Ended, EndReason = DirectCallEndReason.Completed };
        var response = new TaskCompletionSource<DirectCallDTO>();
        fake.CallHandler = (_, _, _) => response.Task;
        var seen = new List<DirectCallState>();
        session.DirectCallChanged += call => seen.Add(call.State);
        var request = session.ChangeDirectCall(7, id, DirectCallAction.Start);
        fake.Raise(new DirectCallChangedEvent(active));
        fake.Raise(new DirectCallChangedEvent(ended));
        response.SetResult(ringing);
        await request;
        Assert.Equal(ended, session.DirectCall);
        Assert.Equal(new[] { DirectCallState.Active, DirectCallState.Ended }, seen);
    }

    [Fact]
    public async Task DisconnectClearsCallAndIgnoresLateResponse()
    {
        var (gateway, fake) = FakeGateway.Create();
        using var session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance);
        await session.Connect("server", 1, null, null);
        var call = Call(Guid.NewGuid(), DirectCallState.Ringing);
        fake.Raise(new DirectCallChangedEvent(call));
        Assert.NotNull(session.DirectCall);
        var response = new TaskCompletionSource<DirectCallDTO>();
        fake.CallHandler = (_, _, _) => response.Task;
        var request = session.ChangeDirectCall(7, call.CallId, DirectCallAction.End);
        await session.Disconnect();
        response.SetResult(call);
        await request;
        Assert.Null(session.DirectCall);
    }

    [Fact]
    public async Task OldCallEventCannotReplaceANewerInvitation()
    {
        var (gateway, fake) = FakeGateway.Create();
        using var session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance);
        await session.Connect("server", 1, null, null);
        var old = Call(Guid.NewGuid(), DirectCallState.Ended);
        var current = Call(Guid.NewGuid(), DirectCallState.Ringing) with { CreatedAt = old.CreatedAt.AddSeconds(1) };
        fake.Raise(new DirectCallChangedEvent(current));
        fake.Raise(new DirectCallChangedEvent(old));
        Assert.Equal(current, session.DirectCall);
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
