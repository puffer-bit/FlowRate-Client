using Client.Core.Sessions.Hybrid;
using Client.Application.Coordinator;
using Client.Application.Coordinator.Users;
using Client.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Client.Application.Sessions.Hybrid;

public class HybridSessionFactory : IHybridSessionFactory
{
    private readonly IWebRTCTransportFactory _transportFactory;
    private readonly IPeerPlaybackService _peerPlayback;
    private readonly IVoiceActivityService _voiceActivity;
    private readonly IScreenWatchService _screenWatch;
    private readonly ILogger<HybridSession> _sessionLogger;

    public HybridSessionFactory(IWebRTCTransportFactory transportFactory, IPeerPlaybackService peerPlayback,
        IVoiceActivityService voiceActivity, IScreenWatchService screenWatch, ILogger<HybridSession> sessionLogger)
    {
        _transportFactory = transportFactory;
        _peerPlayback = peerPlayback;
        _voiceActivity = voiceActivity;
        _screenWatch = screenWatch;
        _sessionLogger = sessionLogger;
    }

    public IHybridSession CreateHybridSession(HybridSessionModel hybridSessionModel,
        ICoordinatorSession coordinatorSession,
        bool isPlaybackEnabled = true) =>
        new HybridSession(hybridSessionModel, coordinatorSession, _transportFactory, _sessionLogger, isPlaybackEnabled,
            _peerPlayback, _voiceActivity, _screenWatch);
}
