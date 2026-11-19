using System.Runtime.InteropServices;
using Gst;

namespace Client.GStreamer.Interop;

/// <summary>PCM buffers stamped against the playback pipeline clock.</summary>
internal static unsafe class GstPcmNative
{
    [DllImport(NativeLibraries.GStreamer)]
    private static extern nint gst_element_get_clock(nint element);
    [DllImport(NativeLibraries.GStreamer)]
    private static extern ulong gst_element_get_base_time(nint element);
    [DllImport(NativeLibraries.GStreamer)]
    private static extern ulong gst_clock_get_time(nint clock);
    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_object_unref(nint obj);
    [DllImport(NativeLibraries.GStreamer)]
    private static extern nint gst_buffer_new_memdup(nint data, nuint size);
    [DllImport(NativeLibraries.GstApp)]
    private static extern int gst_app_src_push_buffer(nint source, nint buffer);

    public static ulong RunningTime(Element element)
    {
        var clock = gst_element_get_clock(element.Handle);
        if (clock == 0)
            return 0;
        try
        {
            var now = gst_clock_get_time(clock);
            var start = gst_element_get_base_time(element.Handle);
            return now >= start ? now - start : 0;
        }
        finally { gst_object_unref(clock); }
    }

    public static bool Push(Element source, ReadOnlySpan<short> samples, ulong timestamp)
    {
        fixed (short* data = samples)
        {
            var buffer = gst_buffer_new_memdup((nint)data, (nuint)(samples.Length * sizeof(short)));
            if (buffer == 0)
                return false;
            // GstBuffer PTS, DTS and duration, following its mini object and pool.
            Marshal.WriteInt64(buffer, 72, (long)timestamp);
            Marshal.WriteInt64(buffer, 80, -1);
            Marshal.WriteInt64(buffer, 88, 10_000_000);
            return gst_app_src_push_buffer(source.Handle, buffer) == (int)FlowReturn.Ok;
        }
    }
}
