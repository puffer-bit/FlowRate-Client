using Client.Application.Ports;

namespace Client.LibWebRTC.Voice;

/// <summary>
/// A sound we send besides the microphone, the one of a shown screen: its capture gives samples in
/// pieces of any length, and libwebrtc takes them 10 ms at a time.
/// </summary>
internal sealed class OutgoingSound : IDisposable
{
    private const int FrameSamples = IAudioCapture.SampleRate / 100 * IAudioCapture.Channels;

    // Keeps Replace and Dispose apart, which come from different threads; the capture thread never takes it
    private readonly Lock _lock = new();
    private IAudioCapture _capture;
    private readonly IAudioSendSource _source;
    private readonly short[] _frame = new short[FrameSamples];
    private int _filled;
    private volatile float _volume = 1;
    private volatile bool _isMuted;
    private bool _isDisposed;

    public OutgoingSound(IAudioCapture capture, IAudioSendSource source)
    {
        _capture = capture;
        _source = source;
        _capture.SamplesReady += OnSamples;
    }

    public IAudioSendSource Source => _source;

    /// <summary>The gain of what is sent, 1 keeps it as it is.</summary>
    public double Volume
    {
        set => _volume = (float)value;
    }

    /// <summary>A muted sound sends nothing, as the microphone does.</summary>
    public bool IsMuted
    {
        set => _isMuted = value;
    }

    public bool Start() => _capture.Start();

    /// <summary>
    /// Captures another input into the same stream, so the viewers keep hearing it: the old capture
    /// stops before the new one starts.
    /// </summary>
    public bool Replace(IAudioCapture capture)
    {
        lock (_lock)
        {
            // Stopped meanwhile: the new capture would run for nothing and never be stopped
            if (_isDisposed)
            {
                capture.Dispose();
                return false;
            }

            _capture.SamplesReady -= OnSamples;
            _capture.Dispose();
            _filled = 0;

            _capture = capture;
            _capture.SamplesReady += OnSamples;
            return _capture.Start();
        }
    }

    // On the thread of the capture, one piece at a time
    private void OnSamples(ReadOnlySpan<short> samples)
    {
        while (!samples.IsEmpty)
        {
            var count = Math.Min(samples.Length, FrameSamples - _filled);
            samples[..count].CopyTo(_frame.AsSpan(_filled));
            samples = samples[count..];
            _filled += count;

            if (_filled < FrameSamples)
                return;

            _filled = 0;

            if (_isMuted)
                continue;

            ApplyVolume();
            _source.Push(_frame, IAudioCapture.SampleRate, IAudioCapture.Channels);
        }
    }

    private void ApplyVolume()
    {
        var volume = _volume;

        if (volume == 1)
            return;

        for (var i = 0; i < _frame.Length; i++)
            _frame[i] = (short)Math.Clamp(_frame[i] * volume, short.MinValue, short.MaxValue);
    }

    /// <summary>Stops the capture, then the source it pushed into.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _capture.Dispose();
            _source.Dispose();
        }
    }
}
