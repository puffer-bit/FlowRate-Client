using Client.GStreamer.Interop;
using Gst;

namespace Client.GStreamer.Audio;

/// <summary>
/// Opens and closes the valve of a <see cref="VoiceGate"/> over one buffer instead of at once. The
/// gate opens when the voice is already loud, and a sound that jumps from silence to the middle of a
/// wave is heard by the listeners as a click at the start of every sentence.
/// </summary>
/// <remarks>
/// <para>A one-shot probe on the src pad of the valve does the fading, so managed code sees only the
/// buffer at the edge of each sentence, not the whole stream. Opening unmutes the valve at once and
/// fades in the first buffer it lets through; closing fades out the next buffer and mutes the valve
/// right after it, on the streaming thread, before the following buffer is processed.</para>
/// <para>The fade needs a writable buffer in a format it knows (16-bit or float samples). Without
/// one the valve switches at once, the way it did before.</para>
/// </remarks>
internal sealed class VoiceGateFade
{
    private readonly Element _valve;
    private readonly Pad? _output;

    // Held so the delegate lives as long as the probe that calls it
    private readonly PadProbeCallback _callback;
    private readonly Lock _lock = new();

    // What the gate wants, what the valve does, and whether the first buffer it lets through is
    // still to be faded in
    private bool _isOpen;
    private bool _isMuted = true;
    private bool _isRising;
    private bool _hasProbe;

    /// <param name="valve">A volume element, muted when the branch is built.</param>
    public VoiceGateFade(Element valve)
    {
        _valve = valve;
        _output = valve.GetStaticPad("src");
        _callback = OnBuffer;
    }

    /// <summary>
    /// Opens or closes the valve. With <paramref name="fade"/> it fades over the next buffer,
    /// otherwise it switches at once.
    /// </summary>
    public void Set(bool isOpen, bool fade)
    {
        lock (_lock)
        {
            _isOpen = isOpen;

            if (!fade || _output == null)
            {
                _isRising = false;
                SetMuted(!isOpen);
                return;
            }

            if (isOpen && _isMuted)
            {
                _isRising = true;
                SetMuted(false);
            }

            if (_hasProbe)
                return;

            _hasProbe = true;
            _output.AddProbe(PadProbeType.Buffer, _callback);
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

            if (_isOpen)
            {
                // A muted valve marks what it lets out as a gap: still the silence from before it opened
                if (_isRising && isGap)
                    return PadProbeReturn.Ok;

                if (_isRising)
                    Fade(pad, buffer, isRising: true);

                _isRising = false;
            }
            else if (!_isMuted)
            {
                if (!isGap)
                    Fade(pad, buffer, isRising: false);

                SetMuted(true);
            }

            _hasProbe = false;
            return PadProbeReturn.Remove;
        }
    }

    private void SetMuted(bool isMuted)
    {
        _isMuted = isMuted;
        _valve.SetProperty("mute", isMuted);
    }

    // Scales the buffer by a linear ramp from silence to full, or the other way
    private static unsafe void Fade(Pad pad, IntPtr buffer, bool isRising)
    {
        using var caps = pad.GetCurrentCaps();

        if (caps == null || caps.GetSize() == 0)
            return;

        string? format;
        int channels;

        using (var structure = caps.GetStructure(0))
        {
            format = structure.GetString("format");

            if (!structure.GetInt("channels", out channels) || channels <= 0)
                return;
        }

        if (format is not ("S16LE" or "F32LE") || !GstNative.TryMapWritable(buffer, out var map))
            return;

        try
        {
            if (format == "S16LE")
            {
                var samples = new Span<short>((void*)map.DataPtr, (int)map.Size / sizeof(short));
                var frames = samples.Length / channels;

                for (var index = 0; index < frames * channels; index++)
                    samples[index] = (short)(samples[index] * Gain(index / channels, frames, isRising));
            }
            else
            {
                var samples = new Span<float>((void*)map.DataPtr, (int)map.Size / sizeof(float));
                var frames = samples.Length / channels;

                for (var index = 0; index < frames * channels; index++)
                    samples[index] *= Gain(index / channels, frames, isRising);
            }
        }
        finally
        {
            GstNative.Unmap(buffer, ref map);
        }
    }

    // Starts at silence when rising and ends at it when falling
    private static float Gain(int frame, int frames, bool isRising) =>
        isRising ? (float)frame / frames : 1f - (float)(frame + 1) / frames;
}
