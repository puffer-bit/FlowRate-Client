using System.Runtime.InteropServices;
using Gst.Interop;

namespace Client.GStreamer.Interop;

/// <summary>
/// Running times of buffers and elements, read without wrappers: the readings are made for every
/// buffer of a voice, and a wrapper per buffer would only feed the collector.
/// </summary>
internal static class GstTimingNative
{
    private const ulong ClockTimeNone = ulong.MaxValue;

    [DllImport(NativeLibraries.GStreamer)]
    private static extern ulong gst_element_get_current_running_time(IntPtr element);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_pad_get_sticky_event(IntPtr pad, Gst.EventType type, uint index);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_event_parse_segment(IntPtr @event, out IntPtr segment);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern ulong gst_segment_to_running_time(IntPtr segment, Gst.Format format, ulong position);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_query_new_latency();

    [DllImport(NativeLibraries.GStreamer)]
    private static extern int gst_pad_query(IntPtr pad, IntPtr query);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_query_parse_latency(IntPtr query, out int isLive, out ulong minLatency,
        out ulong maxLatency);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_mini_object_unref(IntPtr miniObject);

    /// <summary>The running time of the clock of an element in nanoseconds, or null without a clock.</summary>
    public static long? GetCurrentRunningTime(IntPtr element) =>
        AsNanoseconds(gst_element_get_current_running_time(element));

    /// <summary>
    /// The running time of a buffer position on a pad in nanoseconds, by the segment the pad holds,
    /// or null before a segment came or outside of it.
    /// </summary>
    public static long? ToRunningTime(IntPtr pad, long position)
    {
        var segmentEvent = gst_pad_get_sticky_event(pad, Gst.EventType.Segment, 0);

        if (segmentEvent == IntPtr.Zero)
            return null;

        try
        {
            gst_event_parse_segment(segmentEvent, out var segment);

            return segment == IntPtr.Zero
                ? null
                : AsNanoseconds(gst_segment_to_running_time(segment, Gst.Format.Time, (ulong)position));
        }
        finally
        {
            gst_mini_object_unref(segmentEvent);
        }
    }

    /// <summary>
    /// The minimum latency a latency query of the pad answers, in nanoseconds: for the src pad of a
    /// mixer, the time a buffer has between its running time and the moment the mixer gives up on it.
    /// </summary>
    public static long? QueryMinLatency(IntPtr pad)
    {
        var query = gst_query_new_latency();

        try
        {
            if (gst_pad_query(pad, query) == 0)
                return null;

            gst_query_parse_latency(query, out _, out var minLatency, out _);
            return AsNanoseconds(minLatency);
        }
        finally
        {
            gst_mini_object_unref(query);
        }
    }

    private static long? AsNanoseconds(ulong time) =>
        time == ClockTimeNone || time > long.MaxValue ? null : (long)time;
}
