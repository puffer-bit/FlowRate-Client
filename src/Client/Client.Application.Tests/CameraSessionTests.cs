using System.Reflection;
using Client.Application.Coordinator;
using Client.Application.Ports;
using Client.Application.Sessions.Hybrid;
using Client.Core.Coordinator;
using Client.Core.Media;
using Client.Core.Sessions.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Enums;
using Shared.Enums.WebRTC;
using Shared.Models.Media;
using Xunit;
using Stub = Client.Application.Tests.HybridSessionNodeWaitingTests.Stub;

namespace Client.Application.Tests;

public class CameraSessionTests
{
    private static OutboundTrack CameraTrack => new()
    {
        Id = "camera", UserId = 1, SSRC = 123, SourceType = MediaSourceType.Camera,
        MediaType = MediaType.Video, MimeType = "video/VP8", PayloadType = 96, ClockRate = 90000
    };

    private static (HybridSession Session, Stub Coordinator, Stub Transport) Create(bool canStart = true)
    {
        var coordinator = DispatchProxy.Create<ICoordinatorSession, Stub>();
        var transport = DispatchProxy.Create<IWebRTCTransport, Stub>();
        var coordinatorStub = (Stub)(object)coordinator;
        var transportStub = (Stub)(object)transport;
        coordinatorStub.Call = (name, _) => name switch
        {
            nameof(ICoordinatorSession.StartVideoShareInHybridSession) => Task.FromResult(new RequestResult<OutboundTrack>(CameraTrack)),
            nameof(ICoordinatorSession.StopVideoShareInHybridSession) => Task.FromResult(RequestResult.Success),
            _ => throw new NotSupportedException(name)
        };
        transportStub.Call = (name, _) => name switch
        {
            nameof(IWebRTCTransport.StartOutbound) => canStart,
            nameof(IWebRTCTransport.SetInboundVoiceMuted) or nameof(IWebRTCTransport.StopOutbound)
                or nameof(IWebRTCTransport.SetOutboundPreviewShown) or nameof(IWebRTCTransport.RegisterInbound)
                or nameof(IWebRTCTransport.UnregisterInbound) or nameof(IDisposable.Dispose) => null,
            _ => throw new NotSupportedException(name)
        };
        var factory = DispatchProxy.Create<IWebRTCTransportFactory, Stub>();
        ((Stub)(object)factory).Call = (_, _) => transport;
        var model = new HybridSessionModel
        {
            Id = 7, Name = "room", Capacity = 10, SessionType = SessionType.Hybrid,
            EngineType = WebRTCEngine.Mediasoup, HostId = 1
        };
        return (new HybridSession(model, coordinator, factory, NullLogger<HybridSession>.Instance), coordinatorStub, transportStub);
    }

    [Fact]
    public async Task CameraStartsWithoutAudio_AutoPreviews_AndStopsIndependentlyOfScreen()
    {
        var (session, coordinator, transport) = Create();
        using (session)
        {
            ScreenShareSettings? actualSettings = null;
            var previous = transport.Call;
            transport.Call = (name, args) =>
            {
                if (name == nameof(IWebRTCTransport.StartOutbound)) actualSettings = (ScreenShareSettings)args![2]!;
                return previous(name, args);
            };
            Assert.True((await session.StartCameraAsync(new CameraVideoInput("usb"),
                new ScreenShareSettings { IsAudioEnabled = true, AudioAppKey = "audio" })).IsSuccess);
            Assert.False(actualSettings!.IsAudioEnabled);
            Assert.Null(actualSettings.AudioAppKey);
            Assert.True(session.IsCameraStarted);
            Assert.True(session.IsWatchingCamera(1));
            Assert.False(session.IsScreenShareStarted);
            session.SetCameraWatch(1, false);
            Assert.False(session.IsWatchingCamera(1));
            Assert.Contains(nameof(IWebRTCTransport.SetOutboundPreviewShown), transport.Calls);
            Assert.True((await session.StopCameraAsync()).IsSuccess);
            Assert.False(session.IsCameraStarted);
            Assert.Empty(session.CameraUsers);
            Assert.Single(coordinator.Calls, call => call == nameof(ICoordinatorSession.StopVideoShareInHybridSession));
        }
    }

    [Fact]
    public async Task FailedCaptureRollsBackProducerAndDoesNotAnnounceCamera()
    {
        var (session, coordinator, _) = Create(canStart: false);
        using (session)
        {
            var announced = false;
            session.CameraChanged += (_, _) => announced = true;
            var result = await session.StartCameraAsync(new CameraVideoInput("missing"), new ScreenShareSettings());
            Assert.Equal(ErrorCode.UnsupportedMedia, result.Error);
            Assert.False(session.IsCameraStarted);
            Assert.False(announced);
            Assert.Contains(nameof(ICoordinatorSession.StopVideoShareInHybridSession), coordinator.Calls);
        }
    }

    [Fact]
    public async Task LostCameraStopsProducerAndReportsFailureWithoutDisconnectingSession()
    {
        var (session, coordinator, transport) = Create();
        using (session)
        {
            await session.StartCameraAsync(new CameraVideoInput("usb"), new ScreenShareSettings());
            var failed = new TaskCompletionSource<HybridSessionError>(TaskCreationOptions.RunContinuationsAsynchronously);
            session.ErrorOccurred += error => failed.TrySetResult(error);
            transport.RaiseEvent(nameof(IWebRTCTransport.VideoCaptureFailed), "camera");
            var error = await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HybridSessionErrorKind.CameraCaptureFailed, error.Kind);
            Assert.False(session.IsCameraStarted);
            Assert.Empty(session.CameraUsers);
            Assert.Contains(nameof(ICoordinatorSession.StopVideoShareInHybridSession), coordinator.Calls);
        }
    }

    [Fact]
    public async Task IncomingCameraIsWatchedAutomaticallyAndCanBeHiddenAndShown()
    {
        var (session, coordinator, transport) = Create();
        using (session)
        {
            var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var previous = transport.Call;
            transport.Call = (name, args) =>
            {
                if (name == nameof(IWebRTCTransport.RegisterInbound)) registered.TrySetResult();
                return previous(name, args);
            };
            var track = new InboundTrack
            {
                Id = "remote-camera", UserId = 2, SSRC = 456, SourceType = MediaSourceType.Camera,
                MediaType = MediaType.Video, MimeType = "video/VP8", PayloadType = 96, ClockRate = 90000
            };
            coordinator.RaiseEvent("HybridSessionUpdated", new PeerTrackStartedEvent(7, 2, track));
            await registered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(session.IsWatchingCamera(2));
            var received = 0;
            session.VideoFrameReady += (_, _, _) => received++;
            transport.RaiseEvent(nameof(IWebRTCTransport.VideoFrameReady), 2, MediaSourceType.Camera, default(RawVideoFrame));
            session.SetCameraWatch(2, false);
            transport.RaiseEvent(nameof(IWebRTCTransport.VideoFrameReady), 2, MediaSourceType.Camera, default(RawVideoFrame));
            Assert.Equal(1, received);
            session.SetCameraWatch(2, true);
            transport.RaiseEvent(nameof(IWebRTCTransport.VideoFrameReady), 2, MediaSourceType.Camera, default(RawVideoFrame));
            Assert.Equal(2, received);
        }
    }
}
