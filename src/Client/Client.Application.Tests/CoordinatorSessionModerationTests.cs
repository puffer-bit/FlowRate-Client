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

        await _session.Connect("server", 1, null, new RegisterLoginPlan());

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
        await _session.Connect("server", 1, null, new RegisterLoginPlan());
        Assert.Equal(CoordinatorState.Denied, _session.ConnectionStatus);
        Assert.Equal(reason, _session.BanReason);
        Assert.Empty(_gateway.SentLogins);
        Assert.False(_gateway.IsOpen);
    }

    [Fact]
    public async Task BanWhileOnline_ShowsReason_ClosesTransport_AndDoesNotScheduleReconnect()
    {
        await _session.Connect("server", 1, null, new RegisterLoginPlan());
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

        await _session.Connect("server", 1, null, new RegisterLoginPlan());

        Assert.Equal(CoordinatorState.AuthenticationFailed, _session.ConnectionStatus);
    }

    [Fact]
    public async Task SetUserStateAndDeleteUser_GoToTheServer()
    {
        await _session.Connect("server", 1, null, new RegisterLoginPlan());

        Assert.True((await _session.SetUserState(7, UserState.Suspended)).IsSuccess);
        Assert.True((await _session.DeleteUser(8)).IsSuccess);

        Assert.Equal([(7, (UserState?)UserState.Suspended), (8, null)], _gateway.ModerationRequests);
    }

    [Fact]
    public async Task UnbanUser_LiftsEveryBanMadeForTheUser_AndNoOther()
    {
        await _session.Connect("server", 1, null, new RegisterLoginPlan());
        Shared.Models.DataTransferObjects.BanDTO Ban(Shared.Enums.User.BanKind kind, int? userId) =>
            new(Guid.NewGuid(), kind, userId, kind == Shared.Enums.User.BanKind.Ip ? "192.0.2.1" : null, "", 1, DateTimeOffset.UtcNow, null);
        var account = Ban(Shared.Enums.User.BanKind.Account, 7);
        var ip = Ban(Shared.Enums.User.BanKind.Ip, 7);
        _gateway.Bans.AddRange([account, Ban(Shared.Enums.User.BanKind.Account, 8), ip, Ban(Shared.Enums.User.BanKind.Ip, null)]);

        Assert.True((await _session.UnbanUser(7)).IsSuccess);

        Assert.Equal([account.Id, ip.Id], _gateway.RevokedBans);
    }

    [Fact]
    public async Task DeletedUser_IsAnnounced()
    {
        await _session.Connect("server", 1, null, new RegisterLoginPlan());
        int? deleted = null;
        _session.UserDeleted += userId => deleted = userId;

        _gateway.Raise(new UserDeletedEvent(7));

        Assert.Equal(7, deleted);
    }
}
