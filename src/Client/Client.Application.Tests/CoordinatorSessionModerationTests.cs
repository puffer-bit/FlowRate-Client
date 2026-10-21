using Client.Application.Coordinator;
using Client.Application.Tests.Fakes;
using Client.Core.Coordinator;
using Client.Core.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Enums.Auth;
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

    [Fact]
    public async Task BannedLogin_IsDenied_AndClosesTheSocket()
    {
        _gateway.LoginResult = AuthResult.Banned;

        await _session.Connect("server", 1, null, null);

        Assert.Equal(CoordinatorState.Denied, _session.ConnectionStatus);
        Assert.False(_gateway.IsOpen);
    }

    [Fact]
    public async Task FailedLogin_IsNotTakenForABan()
    {
        _gateway.LoginResult = AuthResult.AuthFailed;

        await _session.Connect("server", 1, null, null);

        Assert.Equal(CoordinatorState.AuthenticationFailed, _session.ConnectionStatus);
    }

    [Fact]
    public async Task SetUserStateAndDeleteUser_GoToTheServer()
    {
        await _session.Connect("server", 1, null, null);

        Assert.Equal(ModerateUserResult.NoError, await _session.SetUserState(7, UserState.Suspended));
        Assert.Equal(ModerateUserResult.NoError, await _session.DeleteUser(8));

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
