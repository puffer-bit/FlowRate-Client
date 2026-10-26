using Client.Application.Ports;
using Client.LibWebRTC.Voice;
using Microsoft.Extensions.Logging;

namespace Client.LibWebRTC.Native;

/// <summary>
/// Creates the voice transports of the desktop over libwebrtc. Screens are not shared or watched
/// with them.
/// </summary>
public sealed class LibWebRTCTransportFactory(LibWebRTCRuntime runtime, ILoggerFactory loggerFactory)
    : IWebRTCTransportFactory
{
    /// <summary>Whether libwebrtc is there to create transports with; loads it the first time.</summary>
    public bool IsAvailable => runtime.TryLoad();

    public IWebRTCTransport Create(int sessionId, bool isPlaybackEnabled = true)
    {
        if (!runtime.TryLoad())
            throw new InvalidOperationException("libwebrtc is not available");

        return new VoiceTransport(sessionId, listener => new NativeVoicePeer(runtime, listener),
            loggerFactory.CreateLogger<VoiceTransport>(), isPlaybackEnabled);
    }
}
