using System.Diagnostics;

namespace Client.Core.Media;

/// <summary>
/// Counters of a shown stream since it started.
/// </summary>
/// <param name="LateMs">
/// How much later than the fastest one the latest frame of the last couple of seconds arrived, by
/// their timestamps: near 0 for frames that come evenly, hundreds of ms for frames that the network
/// holds back and lets through in a bunch. 0 without timestamps.
/// </param>
public readonly record struct FrameStats(long Received, long Shown, long Dropped, double ReceivedPerSecond,
    double ShownPerSecond, double LateMs = 0);

/// <summary>
/// Decides which of the frames that arrived is shown on a refresh of the monitor.
/// </summary>
/// <remarks>
/// Frames arrive unevenly: the network and the decoder hold some of them back and let others
/// through in a bunch. Shown the moment they arrive, they stutter. The pacer instead places every
/// frame at its timestamp plus a delay: the smallest one seen lately between a timestamp and the
/// arrival of its frame (the fastest a frame made it), plus room for a few frames. A frame is shown
/// on the first refresh at or after its place, one frame per refresh; only when more than two are
/// due at once are the oldest dropped. Without timestamps, or with the pacing off, the newest frame
/// is shown.
/// Enqueue and pick may run on different threads.
/// </remarks>
public sealed class FramePacer<T> : IDisposable where T : class
{
    // How long the smallest delay is remembered: a slower network raises it after this long
    private static readonly long DelayWindowTicks = Stopwatch.Frequency * 2;

    // A timestamp that jumps by more than this, or back, starts the stream over
    private const long DiscontinuityNanoseconds = 1_000_000_000;

    // Frames that may wait beyond the buffer before the oldest are dropped, so the delay cannot grow
    // and a decoder with a fixed pool is not left without buffers
    private const int QueueSlack = 1;

    // Frames due at once that are caught up with on later refreshes instead of being dropped
    private const int MaxBehind = 2;

    private readonly Action<T> _release;
    private readonly Lock _lock = new();
    private readonly Queue<Entry> _queue = new();

    // Smallest delays between timestamp and arrival, oldest first, with when each was seen
    private readonly LinkedList<(long Delay, long SeenAt)> _delays = new();

    private long _lastTimestamp = -1;
    private long _frameInterval;
    private long _received;
    private long _shown;
    private long _dropped;

    // The latest frame of the last window, see FrameStats.LateMs
    private long _lateTicks;
    private long _lateSeenAt;

    private readonly Stopwatch _rateClock = Stopwatch.StartNew();
    private long _rateReceived;
    private long _rateShown;
    private double _receivedPerSecond;
    private double _shownPerSecond;

    /// <param name="release">Gives a frame back that the pacer drops or still holds when disposed.</param>
    public FramePacer(Action<T> release)
    {
        _release = release;
    }

    /// <summary>Frames are placed by their timestamps; otherwise the newest one is always shown.</summary>
    public bool IsTimed { get; set; } = true;

    /// <summary>How many frames of room are kept in the delay.</summary>
    public int BufferFrames { get; set; } = 2;

    /// <summary>
    /// Takes a frame. The pacer owns it from now on and gives it back through the release callback
    /// unless <see cref="Pick"/> hands it out.
    /// </summary>
    /// <param name="timestamp">The timestamp of the frame in nanoseconds, or -1.</param>
    public void Enqueue(T frame, long timestamp)
    {
        var now = Stopwatch.GetTimestamp();
        List<T>? dropped = null;

        lock (_lock)
        {
            _received++;

            if (timestamp < 0 || !IsTimed)
            {
                // Only the newest matters
                dropped = DrainLocked();
                _queue.Enqueue(new Entry(frame, -1, now));
            }
            else
            {
                if (_lastTimestamp >= 0 &&
                    (timestamp <= _lastTimestamp || timestamp - _lastTimestamp > DiscontinuityNanoseconds))
                {
                    _delays.Clear();
                    _frameInterval = 0;
                }
                else if (_lastTimestamp >= 0)
                {
                    var interval = timestamp - _lastTimestamp;
                    // Smoothed, so one late frame does not change the room much
                    _frameInterval = _frameInterval == 0 ? interval : (_frameInterval * 7 + interval) / 8;
                }

                _lastTimestamp = timestamp;
                RememberDelay(ToTicks(timestamp), now);
                RememberLateness(now - ToTicks(timestamp) - _delays.First!.Value.Delay, now);

                _queue.Enqueue(new Entry(frame, timestamp, now));

                while (_queue.Count > BufferFrames + QueueSlack)
                    (dropped ??= []).Add(_queue.Dequeue().Frame);
            }

            if (dropped != null)
                _dropped += dropped.Count;
        }

        if (dropped != null)
            foreach (var item in dropped)
                _release(item);
    }

    /// <summary>
    /// The frame to show on the refresh happening now, or null to keep the one on screen. The
    /// caller owns the frame it gets.
    /// </summary>
    public T? Pick()
    {
        var now = Stopwatch.GetTimestamp();
        T? picked = null;
        List<T>? dropped = null;

        lock (_lock)
        {
            if (_queue.Count == 0)
                return null;

            if (!IsTimed || _queue.Peek().Timestamp < 0 || _delays.Count == 0)
            {
                while (_queue.Count > 1)
                    (dropped ??= []).Add(_queue.Dequeue().Frame);

                picked = _queue.Dequeue().Frame;
            }
            else
            {
                var delay = _delays.First!.Value.Delay + BufferFrames * NanosecondsToTicks(_frameInterval);
                var due = 0;

                foreach (var entry in _queue)
                {
                    if (ToTicks(entry.Timestamp) + delay > now)
                        break;

                    due++;
                }

                // A refresh that came late finds two frames due: the older is shown now and the newer on
                // the next refresh, which a monitor faster than the stream has to spare. Only further
                // behind than that are frames dropped, so the picture never lags by more than one
                while (due > MaxBehind)
                {
                    (dropped ??= []).Add(_queue.Dequeue().Frame);
                    due--;
                }

                if (due > 0)
                    picked = _queue.Dequeue().Frame;
            }

            if (picked != null)
                _shown++;

            if (dropped != null)
                _dropped += dropped.Count;
        }

        if (dropped != null)
            foreach (var item in dropped)
                _release(item);

        return picked;
    }

    /// <summary>Frames wait to be shown.</summary>
    public bool HasPending
    {
        get
        {
            lock (_lock)
                return _queue.Count > 0;
        }
    }

    public FrameStats Stats
    {
        get
        {
            lock (_lock)
            {
                var elapsed = _rateClock.Elapsed.TotalSeconds;

                // Rates over the last second or so
                if (elapsed >= 1)
                {
                    _receivedPerSecond = (_received - _rateReceived) / elapsed;
                    _shownPerSecond = (_shown - _rateShown) / elapsed;
                    _rateReceived = _received;
                    _rateShown = _shown;
                    _rateClock.Restart();
                }

                var lateMs = Stopwatch.GetTimestamp() - _lateSeenAt <= DelayWindowTicks
                    ? _lateTicks * 1000.0 / Stopwatch.Frequency
                    : 0;

                return new FrameStats(_received, _shown, _dropped, _receivedPerSecond, _shownPerSecond, lateMs);
            }
        }
    }

    /// <summary>
    /// Gives back the frames that wait and forgets the timing, as for a new stream.
    /// </summary>
    public void Reset()
    {
        List<T> dropped;

        lock (_lock)
        {
            dropped = DrainLocked();
            _queue.Clear();
            _delays.Clear();
            _lastTimestamp = -1;
            _frameInterval = 0;
            _lateTicks = 0;
        }

        foreach (var item in dropped)
            _release(item);
    }

    public void Dispose() => Reset();

    private List<T> DrainLocked()
    {
        var frames = new List<T>(_queue.Count);

        while (_queue.Count > 0)
            frames.Add(_queue.Dequeue().Frame);

        return frames;
    }

    // Keeps the smallest delay of the window at the front: newer delays that are smaller make the
    // older, larger ones useless
    private void RememberDelay(long timestampTicks, long now)
    {
        var delay = now - timestampTicks;

        while (_delays.Last != null && _delays.Last.Value.Delay >= delay)
            _delays.RemoveLast();

        _delays.AddLast((delay, now));

        while (_delays.First != null && now - _delays.First.Value.SeenAt > DelayWindowTicks && _delays.Count > 1)
            _delays.RemoveFirst();
    }

    // The largest lateness of the window; a smaller one replaces it once it is older than the window
    private void RememberLateness(long lateness, long now)
    {
        if (lateness >= _lateTicks || now - _lateSeenAt > DelayWindowTicks)
        {
            _lateTicks = Math.Max(0, lateness);
            _lateSeenAt = now;
        }
    }

    private static long ToTicks(long nanoseconds) => NanosecondsToTicks(nanoseconds);

    private static long NanosecondsToTicks(long nanoseconds) =>
        (long)(nanoseconds * (Stopwatch.Frequency / 1_000_000_000.0));

    private readonly record struct Entry(T Frame, long Timestamp, long ArrivedAt);
}
