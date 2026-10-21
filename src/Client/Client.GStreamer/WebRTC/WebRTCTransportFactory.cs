using Client.Application.Ports;
using Microsoft.Extensions.Logging;
using Client.GStreamer.Audio;

namespace Client.GStreamer.WebRTC;

public sealed class WebRTCTransportFactory : IWebRTCTransportFactory
{
    private readonly GstAudioDeviceProvider _devices;
    private readonly ILogger<WebRTCTransport> _transportLogger;

    public WebRTCTransportFactory(GstAudioDeviceProvider devices, ILogger<WebRTCTransport> transportLogger)
    {
        _devices = devices;
        _transportLogger = transportLogger;
    }

    public IWebRTCTransport Create(int sessionId, bool isPlaybackEnabled = true) =>
        new WebRTCTransport(sessionId, _devices, _transportLogger, isPlaybackEnabled);
}
