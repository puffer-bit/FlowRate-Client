using Client.GStreamer.Interop;
using Gst;

namespace Client.GStreamer.Audio;

/// <summary>
/// Opens and closes the valve of a <see cref="VoiceGate"/> with a smooth ramp instead of at once. The
/// gate opens when the voice is already loud, and a sound that jumps from silence to the middle of a
/// wave is heard by the listeners as a click at the start of every sentence; a tail cut off short
/// makes the gate heard as it chops the end of every word.
/// </summary>
/// <remarks>
/// <para>A probe on the src pad of the valve does the fading, and only while a ramp runs, so managed
/// code sees only the few buffers at the edge of each sentence, not the whole stream. Opening unmutes
/// the valve and ramps up over <see cref="AttackTime"/>; closing ramps down over
/// <see cref="ReleaseTime"/> and mutes the valve right after the buffer that reached silence, on the
/// streaming thread, before the following buffer is processed.</para>
/// <para>The ramp goes on from where it is when the gate turns around midway, so a word right after
/// a closing tail does not start with a jump either. Its shape is a smoothstep, flat at both ends.</para>
/// <para>The fade needs a writable buffer in a format it knows (16-bit or float samples). Without
/// one the valve switches at once.</para>
/// </remarks>
internal sealed class VoiceGateFade
{
    // Short enough to keep the first syllable, long enough not to click
    private static readonly TimeSpan AttackTime = TimeSpan.FromMilliseconds(8);

    // Lets the tail of a word and the room fade out the way they would on their own. Longer than
    // the shortest hold of the gate: after a short burst the fade does most of the closing, and a
    // word that only just ends there decays instead of stopping.
    private static readonly TimeSpan ReleaseTime = TimeSpan.FromMilliseconds(200);

    private readonly Element _valve;
    private readonly Pad? _output;

    // Held so the delegate lives as long as the probe that calls it
    private readonly PadProbeCallback _callback;
    private readonly Lock _lock = new();

    // What the gate wants, what the valve does, and where the ramp is, from 0 (silent) to 1 (full)
    private bool _isOpen;
    private bool _isMuted = true;
    private double _position;
    private bool _hasProbe;

    /// <param name="valve">A volume element, muted when the branch is built.</param>
    public VoiceGateFade(Element valve)
    {
        _valve = valve;
        _output = valve.GetStaticPad("src");
        _callback = OnBuffer;
    }

    /// <summary>
    /// Opens or closes the valve, ramping over the next buffers.
    /// </summary>
    public void Set(bool isOpen)
    {
        lock (_lock)
        {
            _isOpen = isOpen;

            if (_output == null)
            {
                _position = isOpen ? 1 : 0;
                SetMuted(!isOpen);
                return;
            }

            // The probe comes first: a buffer let through by the unmuted valve before it was in
            // place would go out at full level, with the very click the fade is there to stop
            if (!_hasProbe)
            {
                _hasProbe = true;
                _output.AddProbe(PadProbeType.Buffer, _callback);
            }

            if (isOpen && _isMuted)
            {
                _position = 0;
                SetMuted(false);
            }
        }
    }

    private PadProbeReturn OnBuffer(Pad pad, PadProbeInfo info)
    {
        lock (_lock)
        {
            IntPtr buffer;
            bool isGap;

            // The wrapper holds a reference of its own, and with it the buffer would not be writable
            using (var wrapper = info.GetBuffer())
            {
                if (wrapper == null)
                    return PadProbeReturn.Ok;

                buffer = wrapper.Handle;
                isGap = wrapper.HasFlags(BufferFlags.Gap);
            }

            var target = _isOpen ? 1.0 : 0.0;

            if (_position != target)
            {
                if (isGap)
                {
                    // A muted valve marks what it lets out as a gap: still the silence from before it
                    // opened. Closing over silence has nothing to fade.
                    if (_isOpen)
                        return PadProbeReturn.Ok;

                    _position = 0;
                }
                else if (!TryFade(pad, buffer))
                    _position = target;
            }

            if (_position != target)
                return PadProbeReturn.Ok;

            if (!_isOpen && !_isMuted)
                SetMuted(true);

            _hasProbe = false;
            return PadProbeReturn.Remove;
        }
    }

    private void SetMuted(bool isMuted)
    {
        _isMuted = isMuted;
        _valve.SetProperty("mute", isMuted);
    }

    // Scales the buffer along the ramp and moves the ramp on by its length
    private unsafe bool TryFade(Pad pad, IntPtr buffer)
    {
        using var caps = pad.GetCurrentCaps();

        if (caps == null || caps.GetSize() == 0)
            return false;

        string? format;
        int channels;
        int rate;

        using (var structure = caps.GetStructure(0))
        {
            format = structure.GetString("format");

            if (!structure.GetInt("channels", out channels) || channels <= 0 ||
                !structure.GetInt("rate", out rate) || rate <= 0)
                return false;
        }

        if (format is not ("S16LE" or "F32LE") || !GstNative.TryMapWritable(buffer, out var map))
            return false;

        // The ramp covers its whole length in so many frames
        var step = 1.0 / (rate * (_isOpen ? AttackTime : ReleaseTime).TotalSeconds);
        var delta = _isOpen ? step : -step;

        try
        {
            if (format == "S16LE")
            {
                var samples = new Span<short>((void*)map.DataPtr, (int)map.Size / sizeof(short));
                var frames = samples.Length / channels;

                for (var frame = 0; frame < frames; frame++)
                {
                    var gain = Advance(delta);

                    for (var channel = 0; channel < channels; channel++)
                    {
                        ref var sample = ref samples[frame * channels + channel];
                        sample = (short)(sample * gain);
                    }
                }
            }
            else
            {
                var samples = new Span<float>((void*)map.DataPtr, (int)map.Size / sizeof(float));
                var frames = samples.Length / channels;

                for (var frame = 0; frame < frames; frame++)
                {
                    var gain = Advance(delta);

                    for (var channel = 0; channel < channels; channel++)
                        samples[frame * channels + channel] *= gain;
                }
            }
        }
        finally
        {
            GstNative.Unmap(buffer, ref map);
        }

        return true;
    }

    // Moves the ramp by one frame and returns the gain there
    private float Advance(double delta)
    {
        _position = Math.Clamp(_position + delta, 0, 1);

        return (float)(_position * _position * (3 - 2 * _position));
    }
}
