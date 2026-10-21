using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Application.Tests.Fakes;
using Client.Core.Coordinator;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Client.Application.Tests;

public class CoordinatorSessionSuspendTests
{
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(5);

    private sealed class FakeConnectivity : IConnectivityMonitor
    {
        public bool IsNetworkAvailable { get; private set; } = true;
        public event Action<bool>? Changed;

        public void Set(bool isAvailable)
        {
            IsNetworkAvailable = isAvailable;
            Changed?.Invoke(isAvailable);
        }
    }

    private sealed class Connected(CoordinatorSession session, FakeGateway gateway, FakeConnectivity connectivity,
        List<CoordinatorState> states)
    {
        public CoordinatorSession Session => session;
        public FakeGateway Gateway => gateway;
        public FakeConnectivity Connectivity => connectivity;

        /// <summary>The states since the first connect, read once nothing more is expected.</summary>
        public CoordinatorState[] States
        {
            get
            {
                lock (states)
                    return states.ToArray();
            }
        }

        public async Task WaitForAsync(CoordinatorState state)
        {
            var deadline = DateTime.UtcNow + Soon;
            while (session.ConnectionStatus != state)
            {
                Assert.True(DateTime.UtcNow < deadline, $"Still {session.ConnectionStatus}, not {state}");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }
    }

    /// <param name="suspendDelay">Null closes the connection as soon as the application leaves the screen.</param>
    private static async Task<Connected> ConnectAsync(TimeSpan? suspendDelay = null)
    {
        var (gateway, fake) = FakeGateway.Create();
        var connectivity = new FakeConnectivity();
        var session = new CoordinatorSession(gateway, new NoTrustedCertificates(), NullLoggerFactory.Instance, connectivity)
        {
            SuspendDelay = suspendDelay ?? TimeSpan.Zero
        };

        await session.Connect("server", 1, null, null);
        Assert.Equal(CoordinatorState.Connected, session.ConnectionStatus);

        List<CoordinatorState> states = [];
        session.StateChanged += state =>
        {
            lock (states)
                states.Add(state);
        };

        return new Connected(session, fake, connectivity, states);
    }

    [Fact]
    public async Task Suspend_ClosesTheConnectionAndKeepsTheSession()
    {
        var connected = await ConnectAsync();

        await connected.Session.Suspend();
        await connected.WaitForAsync(CoordinatorState.Suspended);

        Assert.Equal(CoordinatorState.Suspended, Assert.Single(connected.States));
        Assert.False(connected.Gateway.IsOpen);
    }

    [Fact]
    public async Task Suspend_KeepsTheConnectionForAWhile()
    {
        var connected = await ConnectAsync(TimeSpan.FromMinutes(1));

        await connected.Session.Suspend();
        await connected.Session.Resume();

        Assert.Empty(connected.States);
        Assert.True(connected.Gateway.IsOpen);
        Assert.Equal(1, connected.Gateway.Connects);
    }

    [Fact]
    public async Task SuspendedSession_DoesNotReconnectOnItsOwn()
    {
        var connected = await ConnectAsync();
        await connected.Session.Suspend();
        await connected.WaitForAsync(CoordinatorState.Suspended);

        connected.Gateway.LoseConnection();
        connected.Connectivity.Set(true);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(1, connected.Gateway.Connects);
        Assert.Equal(CoordinatorState.Suspended, connected.Session.ConnectionStatus);
    }

    [Fact]
    public async Task ConnectionLostOffTheScreen_IsNotRestoredUntilTheApplicationIsBack()
    {
        var connected = await ConnectAsync(TimeSpan.FromMinutes(1));
        await connected.Session.Suspend();

        connected.Gateway.LoseConnection();
        await connected.WaitForAsync(CoordinatorState.Suspended);
        Assert.Equal(1, connected.Gateway.Connects);

        await connected.Session.Resume().WaitAsync(Soon, TestContext.Current.CancellationToken);

        Assert.Equal(2, connected.Gateway.Connects);
        Assert.Equal(CoordinatorState.Connected, connected.Session.ConnectionStatus);
    }

    [Fact]
    public async Task Resume_ReconnectsWithoutThePauseOfALostConnection()
    {
        var connected = await ConnectAsync();
        await connected.Session.Suspend();
        await connected.WaitForAsync(CoordinatorState.Suspended);

        await connected.Session.Resume().WaitAsync(TimeSpan.FromMilliseconds(700), TestContext.Current.CancellationToken);

        Assert.Equal(2, connected.Gateway.Connects);
        Assert.Equal([CoordinatorState.Suspended, CoordinatorState.Connecting, CoordinatorState.Connected], connected.States);
    }

    [Fact]
    public async Task Resume_WithoutSuspend_DoesNothing()
    {
        var connected = await ConnectAsync();

        await connected.Session.Resume();

        Assert.Equal(1, connected.Gateway.Connects);
        Assert.Empty(connected.States);
    }

    [Fact]
    public async Task SuspendAndResumeInARow_EndConnected()
    {
        var connected = await ConnectAsync();
        connected.Gateway.DisconnectDuration = TimeSpan.FromMilliseconds(100);

        for (var round = 0; round < 5; round++)
        {
            _ = connected.Session.Suspend();
            await Task.Delay(round * 20, TestContext.Current.CancellationToken);
            _ = connected.Session.Resume();
        }

        await connected.Session.Resume();
        await connected.WaitForAsync(CoordinatorState.Connected);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.Equal(CoordinatorState.Connected, connected.Session.ConnectionStatus);
        Assert.True(connected.Gateway.IsOpen);
        Assert.Equal(0, connected.Gateway.OverlappingCalls);
    }

    [Fact]
    public async Task Disconnect_WhileSuspended_EndsTheSession()
    {
        var connected = await ConnectAsync();
        await connected.Session.Suspend();
        await connected.WaitForAsync(CoordinatorState.Suspended);

        await connected.Session.Disconnect();
        await connected.Session.Resume();

        Assert.Equal(CoordinatorState.Disconnected, connected.Session.ConnectionStatus);
        Assert.Equal(1, connected.Gateway.Connects);
    }

    [Fact]
    public async Task LostConnection_IsRestored()
    {
        var connected = await ConnectAsync();

        connected.Gateway.LoseConnection();
        await connected.WaitForAsync(CoordinatorState.Reconnecting);
        await connected.WaitForAsync(CoordinatorState.Connected);

        Assert.Equal(2, connected.Gateway.Connects);
        Assert.Equal([CoordinatorState.Reconnecting, CoordinatorState.Connected], connected.States);
    }

    [Fact]
    public async Task ReturningNetwork_RetriesAtOnce()
    {
        var connected = await ConnectAsync();
        connected.Gateway.IsReachable = false;
        connected.Connectivity.Set(false);

        connected.Gateway.LoseConnection();
        await connected.WaitForAsync(CoordinatorState.Reconnecting);

        // Without a network the next attempt is half a minute away
        connected.Gateway.IsReachable = true;
        connected.Connectivity.Set(true);
        await connected.WaitForAsync(CoordinatorState.Connected);
    }

    [Fact]
    public async Task ChangedNetwork_ChecksTheConnectionAndRestoresADeadOne()
    {
        var connected = await ConnectAsync();
        connected.Session.CheckTimeout = TimeSpan.FromMilliseconds(50);

        connected.Connectivity.Set(true);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.Equal(1, connected.Gateway.Checks);
        Assert.Empty(connected.States);

        connected.Gateway.IsAnswering = false;
        connected.Connectivity.Set(true);
        await connected.WaitForAsync(CoordinatorState.Reconnecting);
        await connected.WaitForAsync(CoordinatorState.Connected);

        Assert.Equal(2, connected.Gateway.Connects);
    }

    [Fact]
    public async Task Disconnect_WhileReconnecting_StopsTheAttempts()
    {
        var connected = await ConnectAsync();
        connected.Gateway.IsReachable = false;

        connected.Gateway.LoseConnection();
        await connected.WaitForAsync(CoordinatorState.Reconnecting);
        await connected.Session.Disconnect();

        var attempts = connected.Gateway.Connects;
        connected.Gateway.IsReachable = true;
        connected.Connectivity.Set(true);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(CoordinatorState.Disconnected, connected.Session.ConnectionStatus);
        Assert.Equal(attempts, connected.Gateway.Connects);
    }
}
