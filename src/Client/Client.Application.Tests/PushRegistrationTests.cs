using Client.Application.Coordinator;
using Client.Application.Push;
using Client.Application.Tests.Fakes;
using Client.Core.Push;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Enums;
using Shared.Validation;
using Xunit;

namespace Client.Application.Tests;

public class PushRegistrationTests
{
    private readonly MemoryPushKeys _keys = new();
    private readonly FixedPushToken _token = new("token-1");
    private readonly FakeGateway _gateway;
    private readonly CoordinatorSession _session;

    public PushRegistrationTests()
    {
        (var gateway, _gateway) = FakeGateway.Create();
        _session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance)
        {
            // Leaving the screen closes the connection at once, so coming back is a login
            SuspendDelay = TimeSpan.Zero
        };
    }

    private PushRegistration Track(FixedPushToken? token)
    {
        var registration = new PushRegistration(_keys, NullLogger<PushRegistration>.Instance, token);
        registration.Track(_session);
        return registration;
    }

    private async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.True(condition());
    }

    [Fact]
    public async Task Login_KeepsTheReplyTokenWithTheAddressOfTheCoordinator()
    {
        Track(_token);

        await _session.Connect("server", 1, null, null);
        await WaitForAsync(() => _keys.ReplyTargets.ContainsKey(FakeGateway.CoordinatorId));

        Assert.Equal(new PushReplyTarget("server", 1, "reply-token"), _keys.ReplyTargets[FakeGateway.CoordinatorId]);
    }

    [Fact]
    public async Task Login_KeepsNoReplyTokenWhenPushIsDisabled()
    {
        _gateway.PushResult = PushTokenResult.PushDisabled;
        Track(_token);

        await _session.Connect("server", 1, null, null);
        await WaitForAsync(() => _gateway.Registered.Count == 1);

        Assert.Empty(_keys.ReplyTargets);
    }

    [Fact]
    public async Task Login_RegistersTheTokenWithANewKeyOfTheCoordinator()
    {
        Track(_token);

        await _session.Connect("server", 1, null, null);
        await WaitForAsync(() => _gateway.Registered.Count == 1);

        var (token, platform, key) = _gateway.Registered[0];
        Assert.Equal("token-1", token);
        Assert.Equal(PushPlatform.Android, platform);
        Assert.Equal(FakeGateway.CoordinatorId, key.CoordinatorInstanceId);
        Assert.Equal(Limits.PushKeyLength, key.Key.Length);
        Assert.Equal(Limits.PushKeyIdLength * 2, key.KeyId.Length);
        Assert.Same(key, _keys.Keys[FakeGateway.CoordinatorId]);
    }

    [Fact]
    public async Task EveryLogin_RegistersAgainWithTheSameKey()
    {
        Track(_token);
        await _session.Connect("server", 1, null, null);
        await WaitForAsync(() => _gateway.Registered.Count == 1);

        await _session.Suspend();
        await _session.Resume();
        await WaitForAsync(() => _gateway.Registered.Count == 2);

        Assert.Same(_gateway.Registered[0].Key, _gateway.Registered[1].Key);
    }

    [Fact]
    public async Task ReplacedToken_IsRegisteredAtTheConnectedCoordinator()
    {
        Track(_token);
        await _session.Connect("server", 1, null, null);
        await WaitForAsync(() => _gateway.Registered.Count == 1);

        _token.Replace("token-2");
        await WaitForAsync(() => _gateway.Registered.Count == 2);

        Assert.Equal("token-2", _gateway.Registered[1].Token);
    }

    [Fact]
    public async Task Unregister_TellsTheCoordinatorAndForgetsTheKey()
    {
        var registration = Track(_token);
        await _session.Connect("server", 1, null, null);
        await WaitForAsync(() => _gateway.Registered.Count == 1);

        await registration.UnregisterAsync(_session);

        Assert.Equal(["token-1"], _gateway.Unregistered);
        Assert.Empty(_keys.Keys);
    }

    [Fact]
    public async Task WithoutAPushService_NothingIsRegistered()
    {
        var registration = Track(null);

        await _session.Connect("server", 1, null, null);
        await registration.UnregisterAsync(_session);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Empty(_gateway.Registered);
        Assert.Empty(_gateway.Unregistered);
        Assert.Empty(_keys.Keys);
    }

    [Fact]
    public async Task DeviceWithoutAToken_IsNotRegistered()
    {
        _token.Token = null;
        Track(_token);

        await _session.Connect("server", 1, null, null);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Empty(_gateway.Registered);
        Assert.Empty(_keys.Keys);
    }
}
