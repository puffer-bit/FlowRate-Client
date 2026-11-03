using System.Reflection;
using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Application.Sessions.Hybrid;
using Client.Core.Coordinator;
using Client.Core.Sessions.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Enums;
using Shared.Enums.WebRTC;
using Xunit;

namespace Client.Application.Tests;

public class HybridSessionNodeWaitingTests
{
    private static readonly TransportParameters Parameters = new("localhost", 40000,
        new DtlsParameters("auto", []), new IceParameters("ufrag", "password", []));

    private static (HybridSession Session, Stub Coordinator, Stub Transport) CreateSession(
        Func<Task<RequestResult<TransportParameters?>>> join)
    {
        var coordinator = DispatchProxy.Create<ICoordinatorSession, Stub>();
        var coordinatorStub = (Stub)(object)coordinator;
        coordinatorStub.Call = (name, _) => name switch
        {
            "get_ConnectionStatus" => CoordinatorState.Connected,
            nameof(ICoordinatorSession.JoinHybridSession) => join(),
            _ => throw new NotSupportedException(name)
        };
        var transport = DispatchProxy.Create<IWebRTCTransport, Stub>();
        var transportStub = (Stub)(object)transport;
        transportStub.Call = (name, _) => name switch
        {
            "get_ConnectionState" => WebRTCConnectionState.New,
            nameof(IWebRTCTransport.Connect) => true,
            nameof(IWebRTCTransport.WaitUntilConnectedAsync) => Task.FromResult(true),
            nameof(IWebRTCTransport.SetInboundVoiceMuted) or nameof(IDisposable.Dispose) => null,
            _ => throw new NotSupportedException(name)
        };
        var factory = DispatchProxy.Create<IWebRTCTransportFactory, Stub>();
        ((Stub)(object)factory).Call = (_, _) => transport;
        var model = new HybridSessionModel
        {
            Id = 1, Name = "room", Capacity = 10, SessionType = SessionType.Hybrid,
            EngineType = WebRTCEngine.Mediasoup, HostId = 1
        };
        return (new HybridSession(model, coordinator, factory, NullLogger<HybridSession>.Instance),
            coordinatorStub, transportStub);
    }

    [Fact]
    public async Task JoinWithoutTransport_WaitsWithoutStartingMedia_ThenRecoversOnNodeNotification()
    {
        var (session, coordinator, transport) = CreateSession(() =>
            Task.FromResult(new RequestResult<TransportParameters?>(Parameters)));
        using (session)
        {
            var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.StateChanged += state =>
            {
                if (state == HybridSessionState.Connected) connected.TrySetResult();
            };

            session.Connect(null);

            Assert.Equal(HybridSessionState.WaitingForSfuNode, session.State);
            await transport.WaitingObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.DoesNotContain(nameof(IWebRTCTransport.Connect), transport.Calls);

            coordinator.Raise("HybridSessionNodeChanged", new SessionNodeStateChangedEvent(1, HybridSessionNodeState.Active));
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(HybridSessionState.Connected, session.State);
            Assert.Contains(nameof(IWebRTCTransport.Connect), transport.Calls);
        }
    }

    [Fact]
    public async Task RejoinWithoutTransport_KeepsWaitingAndAcceptsALaterRecovery()
    {
        var repliedWithoutTransport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var available = false;
        var (session, coordinator, transport) = CreateSession(() =>
        {
            repliedWithoutTransport.TrySetResult();
            return Task.FromResult(new RequestResult<TransportParameters?>(available ? Parameters : null));
        });
        using (session)
        {
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.StateChanged += state =>
            {
                if (state == HybridSessionState.WaitingForSfuNode) waiting.TrySetResult();
                if (state == HybridSessionState.Connected) connected.TrySetResult();
            };

            coordinator.Raise("HybridSessionNodeChanged", new SessionNodeReplacedEvent(1));
            await repliedWithoutTransport.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await transport.WaitingObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.DoesNotContain(nameof(IWebRTCTransport.Connect), transport.Calls);

            available = true;
            coordinator.Raise("HybridSessionNodeChanged", new SessionNodeStateChangedEvent(1, HybridSessionNodeState.Active));
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(HybridSessionState.Connected, session.State);
        }
    }

    public class Stub : DispatchProxy
    {
        public Func<string, object?[]?, object?> Call { get; set; } = null!;
        public System.Collections.Concurrent.ConcurrentQueue<string> Calls { get; } = new();
        public TaskCompletionSource WaitingObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<string, Delegate?> _events = [];

        public void Raise(string name, HybridSessionNodeEvent value) =>
            ((Action<HybridSessionNodeEvent>?)_events.GetValueOrDefault(name))?.Invoke(value);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            if (name.StartsWith("add_"))
            {
                _events[name[4..]] = Delegate.Combine(_events.GetValueOrDefault(name[4..]), (Delegate?)args![0]);
                return null;
            }
            if (name.StartsWith("remove_"))
            {
                _events[name[7..]] = Delegate.Remove(_events.GetValueOrDefault(name[7..]), (Delegate?)args![0]);
                return null;
            }
            Calls.Enqueue(name);
            if (name == "get_ConnectionState") WaitingObserved.TrySetResult();
            return Call(name, args);
        }
    }
}
