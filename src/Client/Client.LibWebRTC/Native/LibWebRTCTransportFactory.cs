using Client.Application.Ports;
using Client.LibWebRTC.Transport;
using Microsoft.Extensions.Logging;

namespace Client.LibWebRTC.Native;

/// <summary>
/// Creates the transports of the desktop over libwebrtc: the voice, and the screens, which
/// <see cref="IVideoMedia"/> captures, encodes and decodes, and whose sound <see cref="IAudioMedia"/>
/// captures.
/// </summary>
public sealed class LibWebRTCTransportFactory(LibWebRTCRuntime runtime, IVideoMedia video, IAudioMedia audio,
    NativeVideoReceiver videoReceiver, ILoggerFactory loggerFactory) : IWebRTCTransportFactory
{
    /// <summary>Whether libwebrtc is there to create transports with; loads it the first time.</summary>
    public bool IsAvailable => runtime.TryLoad();

    public IWebRTCTransport Create(int sessionId, bool isPlaybackEnabled = true)
    {
        if (!runtime.TryLoad())
            throw new InvalidOperationException("libwebrtc is not available");

        return new LibWebRTCTransport(sessionId, listener => new NativeMediaPeer(runtime, listener),
            loggerFactory.CreateLogger<LibWebRTCTransport>(), isPlaybackEnabled, video, videoReceiver, audio);
    }
}
