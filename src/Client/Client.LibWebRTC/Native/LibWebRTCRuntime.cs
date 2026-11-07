using Client.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Client.LibWebRTC.Native;

/// <summary>The native codec and transport runtime, with audio hardware owned by the media engine.</summary>
public sealed partial class LibWebRTCRuntime(IVoiceAudioDevice audio, ILogger<LibWebRTCRuntime> logger)
    : ILibWebRTCLoader
{
    private readonly Lock _lock = new();
    private readonly Lock _peerLock = new();
    private int _peerCount;
    private bool? _isLoaded;
    private string? _loadError;

    internal IVoiceAudioDevice Audio => audio;

    public bool TryLoad()
    {
        if (!Client.Core.Media.MediaRuntime.IsEnabled)
        {
            _loadError = "Media is disabled by --no-media";
            return false;
        }

        lock (_lock)
        {
            if (_isLoaded is { } loaded)
                return loaded;
            try
            {
                // Reject old prebuilts before starting their platform audio device.
                if (NativeMethods.AudioBridgeVersion() != 1 || NativeMethods.Initialize() == 0)
                    throw new InvalidOperationException("libwebrtc has no compatible external audio device");
                _isLoaded = true;
                LogLoaded();
            }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException
                or BadImageFormatException or InvalidOperationException)
            {
                _isLoaded = false;
                _loadError = error.Message;
                LogLoadFailed(error.Message);
            }
            return _isLoaded.Value;
        }
    }

    public string? Load() => TryLoad() ? null : _loadError;

    internal unsafe nint CreatePeer(NativeMethods.PeerCallbacks* callbacks, nint context, int echoCancellation,
        int noiseSuppression, int autoGainControl)
    {
        lock (_peerLock)
        {
            if (_peerCount == 0 && !audio.StartPlayback(ReadPlayout))
                return 0;
            var peer = NativeMethods.PeerCreate(callbacks, context, 0, 0, 0);
            if (peer != 0)
                _peerCount++;
            else if (_peerCount == 0)
                audio.StopPlayback();
            return peer;
        }
    }

    private static unsafe void ReadPlayout(Span<short> samples)
    {
        if (samples.Length != 960)
            throw new ArgumentException("Playout requires 480 stereo frames", nameof(samples));
        fixed (short* data = samples)
            NativeMethods.PullPlayout(data);
    }

    internal void DestroyPeer(nint peer)
    {
        lock (_peerLock)
        {
            NativeMethods.PeerDestroy(peer);
            if (--_peerCount == 0)
                audio.StopPlayback();
        }
    }

    [LoggerMessage(LogLevel.Information, "libwebrtc loaded with external audio")]
    private partial void LogLoaded();
    [LoggerMessage(LogLevel.Warning, "libwebrtc is not available: {Error}")]
    private partial void LogLoadFailed(string error);
}
