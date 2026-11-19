namespace Client.Core.Media;

/// <summary>How long something took over a window, in milliseconds.</summary>
/// <param name="Count">The measurements in the window; the rest is 0 without any.</param>
public readonly record struct LatencyStats(double P50Ms, double P95Ms, double MaxMs, int Count)
{
    public bool IsEmpty => Count == 0;
}

/// <summary>The average and the longest of durations measured elsewhere, as in flowrate_webrtc.</summary>
public readonly record struct TimingStats(double AverageMs, double MaxMs, long Count)
{
    public bool IsEmpty => Count == 0;
}

/// <summary>
/// Collects durations between two takes and tells their median, 95th percentile and maximum. Made for
/// a measurement per frame: adding allocates nothing, and past the capacity the oldest ones are
/// overwritten, so a window of a few seconds at hundreds of frames a second stays bounded.
/// </summary>
/// <remarks>Thread-safe: frames are measured on streaming threads and taken on a timer.</remarks>
public sealed class LatencyWindow(int capacity = 1024)
{
    private readonly double[] _samples = new double[capacity];
    private readonly Lock _lock = new();
    private int _count;
    private int _next;
    private double _max;

    public void Add(double milliseconds)
    {
        if (milliseconds < 0 || double.IsNaN(milliseconds))
            return;

        lock (_lock)
        {
            _samples[_next] = milliseconds;
            _next = (_next + 1) % _samples.Length;
            _count = Math.Min(_count + 1, _samples.Length);

            // Kept apart, so a spike overwritten by later frames is still the maximum of the window
            _max = Math.Max(_max, milliseconds);
        }
    }

    /// <summary>The statistics of what was added since the previous take, and starts a new window.</summary>
    public LatencyStats Take()
    {
        double[] sorted;
        double max;

        lock (_lock)
        {
            if (_count == 0)
                return default;

            sorted = _samples.AsSpan(0, _count).ToArray();
            max = _max;
            _count = 0;
            _next = 0;
            _max = 0;
        }

        Array.Sort(sorted);

        return new LatencyStats(Percentile(sorted, 0.5), Percentile(sorted, 0.95), max, sorted.Length);
    }

    private static double Percentile(double[] sorted, double share) =>
        sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(share * sorted.Length) - 1)];
}
