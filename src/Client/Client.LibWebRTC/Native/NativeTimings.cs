using Client.Core.Media;

namespace Client.LibWebRTC.Native;

/// <summary>
/// The timings flowrate_webrtc measures of the video, which a library built before them lacks: the
/// first call that misses its function turns them off until the process restarts.
/// </summary>
internal static class NativeTimings
{
    public static volatile bool IsAvailable = true;

    public static TimingStats ToStats(long count, long sumUs, long maxUs) =>
        new(count > 0 ? sumUs / 1000.0 / count : 0, maxUs / 1000.0, count);
}
