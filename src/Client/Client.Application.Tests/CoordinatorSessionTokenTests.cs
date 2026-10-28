using Client.Application.Coordinator;
using Client.Application.Tests.Fakes;
using Client.Core.Coordinator;
using Client.Core.Events;
using Client.Core.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Enums.User;
using Xunit;

namespace Client.Application.Tests;

public class CoordinatorSessionTokenTests
{
    private static readonly LoginChallenge Code = new(false, true, false);

    private readonly List<LoginPrompt> _prompts = [];
    private readonly List<string?> _keptTokens = [];

    private (CoordinatorSession Session, FakeGateway Gateway) Create(LoginChallenge? challenge = null)
    {
        var (gateway, fake) = FakeGateway.Create();
        fake.Challenge = challenge;
        fake.TwoFactorCode = "123456";

        var session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance)
        {
            SuspendDelay = TimeSpan.Zero,
            LoginPrompt = (prompt, _) =>
            {
                _prompts.Add(prompt);
                return Task.FromResult<LoginAnswer?>(new LoginAnswer(null, "123456", false));
            }
        };
        session.SessionTokenChanged += _keptTokens.Add;
        return (session, fake);
    }

    private static async Task ReconnectAsync(CoordinatorSession session)
    {
        await session.Suspend();
        await session.Resume();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.True(condition());
    }

    [Fact]
    public async Task Login_KeepsTheTokenTheServerGave()
    {
        var (session, gateway) = Create();

        await session.Connect("server", 1, "#identity", null);

        Assert.Equal((("#identity", (string?)null)), Assert.Single(gateway.SentLogins));
        Assert.Equal("token-1", Assert.Single(_keptTokens));
        Assert.Equal("token-1", session.GetCurrentUserCredentials().SessionToken);
    }

    [Fact]
    public async Task Reconnect_LogsInWithTheLastToken_WithoutTheSecondStage()
    {
        var (session, gateway) = Create(Code);

        await session.Connect("server", 1, "#identity", null);
        await ReconnectAsync(session);
        await ReconnectAsync(session);

        Assert.Equal(CoordinatorState.Connected, session.ConnectionStatus);
        Assert.Single(_prompts);
        Assert.Equal(["token-1", "token-2"], gateway.SentLogins.Skip(1).Select(login => login.SessionToken));
        Assert.Equal(["token-1", "token-2", "token-3"], _keptTokens);
        // A login with the token is not told the identity, which stays the one the device has
        Assert.Equal("#identity", session.GetCurrentUserCredentials().UserIdentity);
    }

    [Fact]
    public async Task SavedToken_LogsInWithoutAsking()
    {
        var (session, gateway) = Create(Code);
        gateway.SessionToken = "saved";

        await session.Connect("server", 1, "#identity", null, sessionToken: "saved");

        Assert.Equal(CoordinatorState.Connected, session.ConnectionStatus);
        Assert.Empty(_prompts);
        Assert.Equal("#identity", session.GetCurrentUserCredentials().UserIdentity);
    }

    [Fact]
    public async Task UnknownToken_GivesWayToTheIdentity()
    {
        var (session, gateway) = Create(Code);

        await session.Connect("server", 1, "#identity", null, sessionToken: "ended");

        Assert.Equal(CoordinatorState.Connected, session.ConnectionStatus);
        Assert.Single(_prompts);
        Assert.Equal(gateway.SessionToken, session.GetCurrentUserCredentials().SessionToken);
    }

    [Fact]
    public async Task EndedSession_DropsTheToken_AndStaysDisconnected()
    {
        var (session, gateway) = Create();
        UserSessionEndReason? reason = null;
        session.SessionEnded += ended => reason = ended;
        await session.Connect("server", 1, "#identity", null);

        gateway.Raise(new SessionEndedEvent(UserSessionEndReason.Revoked));

        await WaitForAsync(() => session.ConnectionStatus == CoordinatorState.Disconnected);
        Assert.Equal(UserSessionEndReason.Revoked, reason);
        Assert.Null(_keptTokens[^1]);
        Assert.False(gateway.IsOpen);
    }
}
