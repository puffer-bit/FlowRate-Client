using Client.Application.Coordinator;
using Client.Application.Tests.Fakes;
using Client.Core.Coordinator;
using Client.Core.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Client.Application.Tests;

public class CoordinatorSessionLoginTests
{
    private static readonly LoginChallenge Password = new(true, false, false);
    private static readonly LoginChallenge Code = new(false, true, false);
    private static readonly LoginChallenge PasswordAndCode = new(true, true, false);

    /// <summary>Answers the questions of the session one by one, and remembers them.</summary>
    private sealed class User(params LoginAnswer?[] answers)
    {
        private int _next;

        public List<LoginPrompt> Prompts { get; } = [];

        public Task<LoginAnswer?> AnswerAsync(LoginPrompt prompt, CancellationToken cancellationToken)
        {
            Prompts.Add(prompt);
            return Task.FromResult(_next < answers.Length ? answers[_next++] : null);
        }
    }

    private static (CoordinatorSession Session, FakeGateway Gateway) Create(LoginChallenge? challenge, User? user = null)
    {
        var (gateway, fake) = FakeGateway.Create();
        fake.Challenge = challenge;
        fake.AccountPassword = "secret-password";
        fake.TwoFactorCode = "123456";

        var session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance)
        {
            SuspendDelay = TimeSpan.Zero
        };
        if (user != null)
            session.LoginPrompt = user.AnswerAsync;

        return (session, fake);
    }

    // Suspended and back at once: the reconnect logs in again, the way a lost connection does
    private static async Task ReconnectAsync(CoordinatorSession session)
    {
        await session.Suspend();
        await session.Resume();
    }

    [Fact]
    public async Task KnownPassword_LogsInWithoutAsking()
    {
        var user = new User();
        var (session, gateway) = Create(Password, user);

        await session.Connect("server", 1, "#identity", null, "secret-password");

        Assert.Equal(CoordinatorState.Connected, session.ConnectionStatus);
        Assert.Empty(user.Prompts);
        Assert.Equal(("secret-password", (string?)null), Assert.Single(gateway.SecondStageAnswers));
        Assert.Equal("secret-password", session.AccountPasswordToSave);
    }

    [Fact]
    public async Task TypedPassword_IsKeptForTheReconnect()
    {
        var user = new User(new LoginAnswer("secret-password", null, false));
        var (session, _) = Create(Password, user);

        await session.Connect("server", 1, "#identity", null);
        await ReconnectAsync(session);

        Assert.Equal(CoordinatorState.Connected, session.ConnectionStatus);
        var prompt = Assert.Single(user.Prompts);
        Assert.True(prompt.AsksPassword);
        Assert.False(prompt.AsksTwoFactorCode);
        // Not asked to be remembered: nothing goes to the saved identity
        Assert.Null(session.AccountPasswordToSave);
    }

    [Fact]
    public async Task Code_IsAskedAtEveryLogin_ThePasswordOnce()
    {
        var user = new User(new LoginAnswer("secret-password", "123456", true), new LoginAnswer(null, "123456", false));
        var (session, gateway) = Create(PasswordAndCode, user);

        await session.Connect("server", 1, "#identity", null);
        await ReconnectAsync(session);

        Assert.Equal(CoordinatorState.Connected, session.ConnectionStatus);
        Assert.Equal(2, user.Prompts.Count);
        Assert.True(user.Prompts[0].AsksPassword);
        Assert.False(user.Prompts[1].AsksPassword);
        Assert.True(user.Prompts[1].AsksTwoFactorCode);
        Assert.Equal([("secret-password", "123456"), ("secret-password", "123456")], gateway.SecondStageAnswers);
        Assert.Equal("secret-password", session.AccountPasswordToSave);
    }

    [Fact]
    public async Task RefusedSavedPassword_IsAskedAgainAndReplaced()
    {
        var user = new User(new LoginAnswer("secret-password", null, true));
        var (session, _) = Create(Password, user);
        List<string?> saved = [];
        session.AccountPasswordToSaveChanged += saved.Add;

        await session.Connect("server", 1, "#identity", null, "old-password");

        Assert.Equal(CoordinatorState.Connected, session.ConnectionStatus);
        var prompt = Assert.Single(user.Prompts);
        Assert.True(prompt.IsRetry);
        Assert.True(prompt.IsPasswordRemembered);
        Assert.Equal(["secret-password"], saved);
    }

    [Fact]
    public async Task GivenUp_EndsTheLoginAndClosesTheSocket()
    {
        var user = new User((LoginAnswer?)null);
        var (session, gateway) = Create(Code, user);

        await session.Connect("server", 1, "#identity", null);

        Assert.Equal(CoordinatorState.AuthenticationNeeded, session.ConnectionStatus);
        Assert.False(gateway.IsOpen);
        Assert.Empty(gateway.SecondStageAnswers);
    }

    [Fact]
    public async Task WrongCodes_EndTheLoginAfterTheTriesOfTheServer()
    {
        var wrong = new LoginAnswer(null, "000000", false);
        var user = new User(wrong, wrong, wrong, wrong);
        var (session, gateway) = Create(Code, user);

        await session.Connect("server", 1, "#identity", null);

        Assert.Equal(CoordinatorState.AuthenticationFailed, session.ConnectionStatus);
        Assert.Equal(3, gateway.SecondStageAnswers.Count);
        Assert.Equal([false, true, true], user.Prompts.Select(prompt => prompt.IsRetry));
    }

    [Fact]
    public async Task PasskeysAlone_CannotBeUsed()
    {
        var user = new User();
        var (session, gateway) = Create(new LoginChallenge(false, false, true), user);

        await session.Connect("server", 1, "#identity", null);

        Assert.Equal(CoordinatorState.PasskeyRequired, session.ConnectionStatus);
        Assert.Empty(user.Prompts);
        Assert.Empty(gateway.SecondStageAnswers);
    }

    [Fact]
    public async Task UserWithoutPassword_ForgetsTheSavedOne()
    {
        var (session, _) = Create(null);
        List<string?> saved = [];
        session.AccountPasswordToSaveChanged += saved.Add;

        await session.Connect("server", 1, "#identity", null, "old-password");

        Assert.Equal(CoordinatorState.Connected, session.ConnectionStatus);
        Assert.Null(session.AccountPasswordToSave);
        Assert.Equal([(string?)null], saved);
    }

    [Fact]
    public async Task ChangedPassword_IsUsedByTheReconnect()
    {
        var user = new User();
        var (session, gateway) = Create(Password, user);
        await session.Connect("server", 1, "#identity", null, "secret-password");

        var (result, _) = await session.SetAccountPassword(new AccountProof("secret-password", null), "new-password");
        await ReconnectAsync(session);

        Assert.True(result.IsSuccess);
        Assert.Equal(CoordinatorState.Connected, session.ConnectionStatus);
        Assert.Empty(user.Prompts);
        Assert.Equal("new-password", gateway.SecondStageAnswers[^1].Password);
        Assert.Equal("new-password", session.AccountPasswordToSave);
    }
}
