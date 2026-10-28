using Client.Application.Ports;
using Client.GStreamer.WebRTC;
using Client.LibWebRTC.Native;
using Microsoft.Extensions.Logging;

namespace Client.Services.Media;

/// <summary>
/// Creates the transport of each session on the engine the experimental setting picks: GStreamer,
/// or libwebrtc for the voice and the screens. Falls back to GStreamer when libwebrtc is not there.
/// </summary>
internal sealed partial class MediaEngineTransportFactory(
    WebRTCTransportFactory gstreamer,
    LibWebRTCTransportFactory libWebRTC,
    ILogger<MediaEngineTransportFactory> logger) : IWebRTCTransportFactory
{
    public IWebRTCTransport Create(int sessionId, bool isPlaybackEnabled = true)
    {
        if (ExperimentalFeatures.UseLibWebRTC)
        {
            if (libWebRTC.IsAvailable)
            {
                LogEngine(sessionId, "libwebrtc");
                return libWebRTC.Create(sessionId, isPlaybackEnabled);
            }

            LogLibWebRTCMissing(sessionId);
        }

        return gstreamer.Create(sessionId, isPlaybackEnabled);
    }

    [LoggerMessage(LogLevel.Information, "[Session {SessionId}] Media over {Engine}")]
    private partial void LogEngine(int sessionId, string engine);

    [LoggerMessage(LogLevel.Warning, "[Session {SessionId}] libwebrtc is chosen but missing, the media goes over GStreamer")]
    private partial void LogLibWebRTCMissing(int sessionId);
}
