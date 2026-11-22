using System.Diagnostics;
using Client.Core.Settings;

namespace Client.LibWebRTC.Transport;

/// <summary>
/// When the transport reads the statistics, and which readings carry what: every reading has the
/// levels for the speaking indicator, a detailed one the counters of the streams and, while they are
/// measured, the statistics of the media.
/// </summary>
internal sealed class StatsSchedule
{
    // How often the statistics are read: often enough for the speaking indicator, which goes by
    // the levels in them
    public static readonly TimeSpan StatsInterval = TimeSpan.FromMilliseconds(200);

    // How often a reading carries the counters of the streams: a peer that has to serialize them, as the
    // one of Android, does it only then. The samples of the transport and the statistics of the media
    // are made of these readings
    private static readonly TimeSpan CountersInterval = TimeSpan.FromSeconds(1);

    // A reading comes a little before or after its time
    private const double IntervalSlack = 0.9;

    private long _detailedRequested;
    private long _countersSampled;
    private long _perfSampled;

    /// <summary>Whether the reading asked for now has to carry the counters and the entries.</summary>
    public bool IsDetailedRequestDue() => IsDue(ref _detailedRequested, DetailedInterval());

    /// <summary>Whether the counters of a reading are turned into a sample of the transport.</summary>
    public bool IsCountersSampleDue() => IsDue(ref _countersSampled, CountersInterval);

    /// <summary>Whether the entries of a reading are turned into the statistics of the media.</summary>
    public bool IsPerfSampleDue() =>
        DebugSettings.Current.IsMediaPerfNeeded && IsDue(ref _perfSampled, DebugSettings.Current.MediaPerfInterval);

    // The shorter of the counters and of the statistics of the media while they are measured
    private static TimeSpan DetailedInterval() =>
        DebugSettings.Current.IsMediaPerfNeeded && DebugSettings.Current.MediaPerfInterval < CountersInterval
            ? DebugSettings.Current.MediaPerfInterval
            : CountersInterval;

    // Whether the interval passed since the time kept in last, which is then moved to now
    private static bool IsDue(ref long last, TimeSpan interval)
    {
        var now = Stopwatch.GetTimestamp();

        if (last != 0 && Stopwatch.GetElapsedTime(last, now) < interval * IntervalSlack)
            return false;

        last = now;
        return true;
    }
}
