using Client.Application.Coordinator;
using Client.Application.Tests.Fakes;
using Client.Core.Coordinator;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Enums;
using Shared.Enums.User;
using Xunit;

namespace Client.Application.Tests;

public class CoordinatorSessionModerationTests
{
    private readonly FakeGateway _gateway;
    private readonly CoordinatorSession _session;

    public CoordinatorSessionModerationTests()
    {
        (var gateway, _gateway) = FakeGateway.Create();
        _session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance);
    }

    [Theory]
    [InlineData("Abuse in chat")]
    [InlineData("")]
    [InlineData(null)]
    public async Task BannedLogin_IsDenied_AndClosesTheSocket(string? reason)
    {
        _gateway.LoginError = ErrorCode.Banned;
        _gateway.LoginErrorDetails = reason;

        await _session.Connect("server", 1, null, null);

        Assert.Equal(CoordinatorState.Denied, _session.ConnectionStatus);
        Assert.False(_gateway.IsOpen);
        Assert.Equal(reason, _session.BanReason);
    }

    [Theory]
    [InlineData("Shared IP blocked")]
    [InlineData("")]
    public async Task IpBanBeforeHandshake_IsDeniedWithReason_AndDoesNotAttemptLogin(string reason)
    {
        _gateway.TransportBanReason = reason;
        await _session.Connect("server", 1, null, null);
        Assert.Equal(CoordinatorState.Denied, _session.ConnectionStatus);
        Assert.Equal(reason, _session.BanReason);
        Assert.Empty(_gateway.SentLogins);
        Assert.False(_gateway.IsOpen);
    }

    [Fact]
    public async Task BanWhileOnline_ShowsReason_ClosesTransport_AndDoesNotScheduleReconnect()
    {
        await _session.Connect("server", 1, null, null);
        var retries = 0;
        _session.ReconnectScheduled += (_, _) => retries++;
        _gateway.Raise(new BannedEvent("Repeated spam"));
        for (var i = 0; i < 200 && _session.ConnectionStatus != CoordinatorState.Denied; i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(CoordinatorState.Denied, _session.ConnectionStatus);
        Assert.Equal("Repeated spam", _session.BanReason);
        Assert.False(_gateway.IsOpen);
        Assert.Equal(0, retries);
        Assert.Equal(1, _gateway.Connects);
    }

    [Fact]
    public async Task FailedLogin_IsNotTakenForABan()
    {
        _gateway.LoginError = ErrorCode.AuthFailed;

        await _session.Connect("server", 1, null, null);

        Assert.Equal(CoordinatorState.AuthenticationFailed, _session.ConnectionStatus);
    }

    [Fact]
    public async Task SetUserStateAndDeleteUser_GoToTheServer()
    {
        await _session.Connect("server", 1, null, null);

        Assert.True((await _session.SetUserState(7, UserState.Suspended)).IsSuccess);
        Assert.True((await _session.DeleteUser(8)).IsSuccess);

        Assert.Equal([(7, (UserState?)UserState.Suspended), (8, null)], _gateway.ModerationRequests);
    }

    [Fact]
    public async Task DeletedUser_IsAnnounced()
    {
        await _session.Connect("server", 1, null, null);
        int? deleted = null;
        _session.UserDeleted += userId => deleted = userId;

        _gateway.Raise(new UserDeletedEvent(7));

        Assert.Equal(7, deleted);
    }
}
