using System.Runtime.InteropServices;
using Client.GStreamer.Interop;
using Gst;

namespace Client.GStreamer.Audio;

/// <summary>
/// Measures how late the decoded buffers of a voice reach the mixer: the running time of the clock
/// when a buffer arrives, less the running time it is to be played at. The mixer throws away what
/// comes after its deadline, so a voice whose delay creeps up towards the latency of the output
/// breaks up more and more, while its packets still arrive in full.
/// </summary>
/// <remarks>
/// The probe runs on a streaming thread for every buffer, so it only adds the delay to the figures
/// of the current period; the transport reads and resets them with <see cref="TakeSummary"/>.
/// </remarks>
public sealed class VoiceArrivalMeter
{
    private readonly IntPtr _mixer;

    // Held so the delegate lives as long as the probe that calls it
    private readonly PadProbeCallback _callback;
    private readonly Lock _lock = new();
    private CULong _probe;

    // The latency of the mixer in ns, past which the mixer drops a buffer; 0 while unknown
    private long _deadline;

    private int _buffers;
    private int _late;
    private long _delaySum;
    private long _minDelay = long.MaxValue;
    private long _maxDelay = long.MinValue;
    private long _lastDelay;

    /// <param name="mixer">The mixer, whose clock the delays are measured on.</param>
    public VoiceArrivalMeter(Element mixer)
    {
        _mixer = mixer.Handle;
        _callback = OnBuffer;
    }

    /// <summary>
    /// Starts measuring the buffers that pass <paramref name="pad"/>, a sink pad of the mixer.
    /// </summary>
    public void Attach(Pad pad) => _probe = pad.AddProbe(PadProbeType.Buffer, _callback);

    /// <summary>
    /// Stops measuring <paramref name="pad"/>, the one given to <see cref="Attach"/>.
    /// </summary>
    public void Detach(Pad pad)
    {
        if (_probe.Value != 0)
            pad.RemoveProbe(_probe);

        _probe = default;
    }

    /// <summary>
    /// The figures since the previous call, in milliseconds, or null when no buffer came.
    /// </summary>
    /// <param name="deadline">
    /// The latency of the mixer in nanoseconds, which the buffers of the next period are counted
    /// late against, or null while it is unknown.
    /// </param>
    public Summary? TakeSummary(long? deadline)
    {
        lock (_lock)
        {
            _deadline = deadline ?? 0;

            if (_buffers == 0)
                return null;

            var summary = new Summary(_buffers, _late, _minDelay / 1e6, _delaySum / 1e6 / _buffers,
                _maxDelay / 1e6, _lastDelay / 1e6);

            _buffers = 0;
            _late = 0;
            _delaySum = 0;
            _minDelay = long.MaxValue;
            _maxDelay = long.MinValue;

            return summary;
        }
    }

    private PadProbeReturn OnBuffer(Pad pad, PadProbeInfo info)
    {
        long pts;

        // The wrapper holds a reference of its own, released here rather than by the GC
        using (var buffer = info.GetBuffer())
        {
            if (buffer == null)
                return PadProbeReturn.Ok;

            pts = GstVideoNative.GetPts(buffer.Handle);
        }

        if (pts < 0 ||
            GstTimingNative.ToRunningTime(pad.Handle, pts) is not { } runningTime ||
            GstTimingNative.GetCurrentRunningTime(_mixer) is not { } now)
            return PadProbeReturn.Ok;

        var delay = now - runningTime;

        lock (_lock)
        {
            _buffers++;

            if (_deadline > 0 && delay > _deadline)
                _late++;

            _delaySum += delay;
            _minDelay = Math.Min(_minDelay, delay);
            _maxDelay = Math.Max(_maxDelay, delay);
            _lastDelay = delay;
        }

        return PadProbeReturn.Ok;
    }

    /// <param name="Buffers">How many buffers came in the period.</param>
    /// <param name="Late">How many of them came past the latency of the output.</param>
    /// <param name="LastMs">The delay of the last of them.</param>
    public sealed record Summary(int Buffers, int Late, double MinMs, double AverageMs, double MaxMs, double LastMs);
}
