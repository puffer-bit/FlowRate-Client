using Client.LibWebRTC.Voice;

namespace Client.LibWebRTC.Native;

/// <summary>
/// A sound we send over flowrate_webrtc, as the sound of a shown screen: an fr_audio_source, which
/// takes 10 ms at a time.
/// </summary>
internal sealed unsafe class NativeAudioSource : IAudioSendSource
{
    private readonly Lock _lock = new();
    private readonly nint _source;

    // The rounds of negotiation that hand the source to the peer now; it is destroyed after the last
    private int _uses;
    private bool _isDisposed;

    private NativeAudioSource(nint source) => _source = source;

    /// <returns>Null when libwebrtc is not loaded.</returns>
    public static NativeAudioSource? Create()
    {
        var source = NativeMethods.AudioSourceCreate();
        return source != 0 ? new NativeAudioSource(source) : null;
    }

    public void Push(ReadOnlySpan<short> samples, int sampleRate, int channels)
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            fixed (short* data = samples)
                NativeMethods.AudioSourcePush(_source, data, samples.Length / channels, sampleRate, channels);
        }
    }

    /// <summary>
    /// The handle for one round of negotiation; 0 once the source is disposed. Pair with
    /// <see cref="Release"/>.
    /// </summary>
    public nint Acquire()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return 0;

            _uses++;
            return _source;
        }
    }

    public void Release()
    {
        lock (_lock)
        {
            if (--_uses == 0 && _isDisposed)
                NativeMethods.AudioSourceDestroy(_source);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            if (_uses == 0)
                NativeMethods.AudioSourceDestroy(_source);
        }
    }
}
