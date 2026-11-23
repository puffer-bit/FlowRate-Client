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

    [Theory]
    [InlineData(Shared.Enums.ErrorCode.OnlineLimitReached, CoordinatorState.OnlineLimitReached)]
    [InlineData(Shared.Enums.ErrorCode.UserLimitReached, CoordinatorState.UserLimitReached)]
    public async Task CapacityRejection_ReportsItsReasonAndClosesSocket(Shared.Enums.ErrorCode error, CoordinatorState expected)
    {
        var (session, gateway) = Create(null);
        using (session)
        {
            gateway.LoginError = error;
            await session.Connect("server", 1, null, new RegisterLoginPlan());
            Assert.Equal(expected, session.ConnectionStatus);
            Assert.False(gateway.IsOpen);
        }
    }

    [Theory]
    [InlineData(Shared.Enums.ErrorCode.OnlineLimitReached, CoordinatorState.OnlineLimitReached)]
    [InlineData(Shared.Enums.ErrorCode.UserLimitReached, CoordinatorState.UserLimitReached)]
    public async Task CapacityRejection_OnResume_StopsAndReportsItsReason(Shared.Enums.ErrorCode error, CoordinatorState expected)
    {
        var (session, gateway) = Create(null);
        using (session)
        {
            await session.Connect("server", 1, null, new RegisterLoginPlan());
            gateway.LoginError = error;
            await ReconnectAsync(session);
            Assert.Equal(expected, session.ConnectionStatus);
            Assert.False(gateway.IsOpen);
        }
    }

    // Suspended and back at once: the reconnect logs in again, the way a lost connection does
    private static async Task ReconnectAsync(CoordinatorSession session)
    {
        await session.Suspend();
        await session.Resume();
    }

    private static readonly LoginPlan ByUsername = new UsernameLoginPlan("alice");

    [Fact]
    public async Task Username_AsksThePassword_AndTheReconnectGoesAsTheNewDevice()
    {
        var user = new User(new LoginAnswer("secret-password", null));
        var (session, gateway) = Create(Password, user);

        await session.Connect("server", 1, null, ByUsername);
        await ReconnectAsync(session);

        Assert.Equal(CoordinatorState.Connected, session.ConnectionStatus);
        var prompt = Assert.Single(user.Prompts);
        Assert.True(prompt.AsksPassword);
        Assert.False(prompt.AsksTwoFactorCode);
        // The reconnect signs the challenge with the key of the device the login added
        Assert.IsType<Shared.Models.DataTransferObjects.DeviceLoginDTO>(gateway.SentLogins[^1]);
        var credentials = session.GetCurrentUserCredentials();
        Assert.Equal(Assert.Single(gateway.Devices).Key, credentials.DeviceId);
        Assert.NotNull(credentials.DeviceKey);
    }

    [Fact]
    public async Task Register_KeepsTheNewRecoveryPhraseUntilTaken()
    {
        var (session, gateway) = Create(null);

        await session.Connect("server", 1, null, new RegisterLoginPlan());

        Assert.Equal(CoordinatorState.Connected, session.ConnectionStatus);
        var register = Assert.IsType<Shared.Models.DataTransferObjects.RegisterLoginDTO>(Assert.Single(gateway.SentLogins));
        var phrase = session.TakeNewRecoveryPhrase();
        Assert.NotNull(phrase);
        Assert.Null(session.TakeNewRecoveryPhrase());
        using var keys = Client.Application.Security.RecoveryKeySet.FromPhrase(phrase!)!;
        Assert.Equal(Shared.Auth.LoginSignature.Encode(keys.RecoveryPublicKey), register.Keys.RecoveryPublicKey);
    }

    [Fact]
    public async Task UnknownDevice_EndsTheDevice()
    {
        var (session, gateway) = Create(null);
        Shared.Enums.Auth.DeviceEndReason? ended = Shared.Enums.Auth.DeviceEndReason.Revoked;
        var endedRaised = false;
        session.DeviceEnded += reason => { ended = reason; endedRaised = true; };

        await session.Connect("server", 1, null, new RegisterLoginPlan());
        gateway.Devices.Clear();
        await ReconnectAsync(session);

        Assert.Equal(CoordinatorState.DeviceEnded, session.ConnectionStatus);
        Assert.True(endedRaised);
        Assert.Null(ended);
        Assert.False(gateway.IsOpen);
    }

    [Fact]
    public async Task DeviceRemovedWhileConnected_StaysOffline()
    {
        var (session, gateway) = Create(null);
        Shared.Enums.Auth.DeviceEndReason? ended = null;
        session.DeviceEnded += reason => ended = reason;
        await session.Connect("server", 1, null, new RegisterLoginPlan());

        gateway.Raise(new DeviceEndedEvent(Shared.Enums.Auth.DeviceEndReason.Revoked));
        await Task.Delay(100);

        Assert.Equal(Shared.Enums.Auth.DeviceEndReason.Revoked, ended);
        Assert.Equal(CoordinatorState.Disconnected, session.ConnectionStatus);
    }

    [Fact]
    public async Task LegacyIdentity_WithItsPassword_MovesOverWithoutAsking()
    {
        var user = new User();
        var (session, gateway) = Create(Password, user);

        await session.Connect("server", 1, null, new LegacyLoginPlan("#identity", null, "secret-password"));

        Assert.Equal(CoordinatorState.Connected, session.ConnectionStatus);
        Assert.Empty(user.Prompts);
        Assert.Equal(("secret-password", (string?)null), Assert.Single(gateway.SecondStageAnswers));
        Assert.NotNull(session.TakeNewRecoveryPhrase());
        Assert.NotNull(session.GetCurrentUserCredentials().DeviceId);
    }

    [Fact]
    public async Task LegacyIdentity_WithItsLiveToken_SkipsTheSecondStage()
    {
        var user = new User();
        var (session, gateway) = Create(Password, user);
        gateway.SessionToken = "saved";

        await session.Connect("server", 1, null, new LegacyLoginPlan("#identity", "saved", null));

        Assert.Equal(CoordinatorState.Connected, session.ConnectionStatus);
        Assert.Empty(user.Prompts);
        Assert.Empty(gateway.SecondStageAnswers);
    }

    [Fact]
    public async Task RefusedPassword_IsAskedAgain()
    {
        var user = new User(new LoginAnswer("wrong", null), new LoginAnswer("secret-password", null));
        var (session, _) = Create(Password, user);

        await session.Connect("server", 1, null, ByUsername);

        Assert.Equal(CoordinatorState.Connected, session.ConnectionStatus);
        Assert.Equal([false, true], user.Prompts.Select(prompt => prompt.IsRetry));
    }

    [Fact]
    public async Task GivenUp_EndsTheLoginAndClosesTheSocket()
    {
        var user = new User((LoginAnswer?)null);
        var (session, gateway) = Create(Code, user);

        await session.Connect("server", 1, null, ByUsername);

        Assert.Equal(CoordinatorState.AuthenticationNeeded, session.ConnectionStatus);
        Assert.False(gateway.IsOpen);
        Assert.Empty(gateway.SecondStageAnswers);
    }

    [Fact]
    public async Task WrongCodes_EndTheLoginAfterTheTriesOfTheServer()
    {
        var wrong = new LoginAnswer(null, "000000");
        var user = new User(wrong, wrong, wrong, wrong);
        var (session, gateway) = Create(Code, user);

        await session.Connect("server", 1, null, ByUsername);

        Assert.Equal(CoordinatorState.LoginRefused, session.ConnectionStatus);
        Assert.Equal(3, gateway.SecondStageAnswers.Count);
        Assert.Equal([false, true, true], user.Prompts.Select(prompt => prompt.IsRetry));
    }

    [Fact]
    public async Task PasskeysAlone_CannotBeUsed()
    {
        var user = new User();
        var (session, gateway) = Create(new LoginChallenge(false, false, true), user);

        await session.Connect("server", 1, null, ByUsername);

        Assert.Equal(CoordinatorState.PasskeyRequired, session.ConnectionStatus);
        Assert.Empty(user.Prompts);
        Assert.Empty(gateway.SecondStageAnswers);
    }

    [Fact]
    public async Task InvalidRecoveryPhrase_IsRefusedHere()
    {
        var (session, gateway) = Create(null);

        await session.Connect("server", 1, null, new RecoveryLoginPlan("not a phrase"));

        Assert.Equal(CoordinatorState.LoginRefused, session.ConnectionStatus);
        Assert.Empty(gateway.SentLogins);
    }

    [Fact]
    public async Task TemporaryDevice_IsNotRemembered()
    {
        var (session, _) = Create(null);

        await session.Connect("server", 1, null, new LoginCodeLoginPlan("ABCD-EFGH-JKMN", Temporary: true));

        Assert.Equal(CoordinatorState.Connected, session.ConnectionStatus);
        Assert.True(session.IsTemporaryDevice);
    }

    [Fact]
    public async Task SavedIdentity_CannotAttachToAnotherCoordinatorAtTheSameAddress()
    {
        var (session, _) = Create(null);
        using (session)
        {
            await session.Connect("server", 1, null, new RegisterLoginPlan(), expectedCoordinatorId: "old-coordinator");
            Assert.Equal(CoordinatorState.CoordinatorMismatch, session.ConnectionStatus);
            Assert.Null(session.CoordinatorModel);
            Assert.Equal("old-coordinator", session.CoordinatorMismatch!.ExpectedCoordinatorId);
        }
    }

    [Fact]
    public async Task Reconnect_RejectsAReplacementCoordinatorEvenWithoutAPinnedCertificate()
    {
        var (session, gateway) = Create(null);
        using (session)
        {
            await session.Connect("server", 1, null, new RegisterLoginPlan());
            Assert.Equal(CoordinatorState.Connected, session.ConnectionStatus);
            gateway.ActiveCoordinatorId = "replacement";
            await ReconnectAsync(session);
            Assert.Equal(CoordinatorState.CoordinatorMismatch, session.ConnectionStatus);
            Assert.Equal(FakeGateway.CoordinatorId, session.CoordinatorModel!.Id);
        }
    }
}
