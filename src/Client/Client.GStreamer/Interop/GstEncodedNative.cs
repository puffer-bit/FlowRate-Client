using System.Runtime.InteropServices;
using Client.Core.Media;
using Gst;

namespace Client.GStreamer.Interop;

/// <summary>
/// Encoded frames between a pipeline and the code around it: reading them out of a buffer, pushing
/// them into an appsrc, and the keyframe requests that travel upstream. Native calls only, so none of
/// it makes a wrapper on a streaming thread.
/// </summary>
public static unsafe class GstEncodedNative
{
    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_buffer_new_memdup(IntPtr data, nuint size);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern int gst_buffer_map(IntPtr buffer, out MapInfo info, MapFlags flags);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_buffer_unmap(IntPtr buffer, ref MapInfo info);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern int gst_element_send_event(IntPtr element, IntPtr @event);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern ulong gst_pad_add_probe(IntPtr pad, int mask,
        delegate* unmanaged<IntPtr, IntPtr, IntPtr, int> callback, IntPtr data,
        delegate* unmanaged<IntPtr, void> destroy);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_pad_probe_info_get_event(IntPtr info);

    [DllImport(NativeLibraries.GstApp)]
    private static extern int gst_app_src_push_buffer(IntPtr appsrc, IntPtr buffer);

    [DllImport(NativeLibraries.GstVideo)]
    private static extern IntPtr gst_video_event_new_upstream_force_key_unit(ulong runningTime, int allHeaders, uint count);

    [DllImport(NativeLibraries.GstVideo)]
    private static extern int gst_video_event_is_force_key_unit(IntPtr @event);

    [DllImport(NativeLibraries.GstVideo)]
    private static extern int gst_video_event_parse_upstream_force_key_unit(IntPtr @event, out ulong runningTime,
        out int allHeaders, out uint count);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern int gst_pad_push_event(IntPtr pad, IntPtr @event);

    // GstBuffer: the mini object (64 bytes), the pool, then pts; the flags of the mini object follow
    // its type, ref count and lock state
    private const int BufferPtsOffset = 72;
    private const int MiniObjectFlagsOffset = 16;

    // GST_BUFFER_FLAG_DELTA_UNIT, GST_MINI_OBJECT_FLAG_LAST << 9
    private const int DeltaUnitFlag = 1 << 13;

    private const ulong ClockTimeNone = ulong.MaxValue;

    private const int ProbeEventUpstream = 1 << 7;
    private const int ProbeDrop = 0;
    private const int ProbeOk = 1;

    /// <summary>
    /// Hands the bytes of a buffer to <paramref name="onFrame"/> as an encoded frame, mapped for as
    /// long as the call lasts.
    /// </summary>
    /// <returns>False when the buffer could not be mapped.</returns>
    public static bool Read(Gst.Buffer buffer, Action<EncodedVideoFrame> onFrame)
    {
        var handle = buffer.Handle;

        if (gst_buffer_map(handle, out var map, MapFlags.Read) == 0)
            return false;

        try
        {
            var pts = (ulong)Marshal.ReadInt64(handle, BufferPtsOffset);
            var isKeyFrame = (Marshal.ReadInt32(handle, MiniObjectFlagsOffset) & DeltaUnitFlag) == 0;

            onFrame(new EncodedVideoFrame(map.DataPtr, (int)map.Size,
                pts == ClockTimeNone || pts > long.MaxValue ? -1 : (long)pts, isKeyFrame));
            return true;
        }
        finally
        {
            gst_buffer_unmap(handle, ref map);
        }
    }

    /// <summary>
    /// Pushes a copy of a frame into an appsrc. The timestamp becomes the PTS of the buffer unless the
    /// appsrc stamps the buffers itself (do-timestamp) or the frame has none.
    /// </summary>
    /// <returns>False when the appsrc refused it, as it does while it is not running.</returns>
    public static bool Push(Element appsrc, EncodedVideoFrame frame)
    {
        var buffer = gst_buffer_new_memdup(frame.Data, (nuint)frame.Length);

        if (buffer == IntPtr.Zero)
            return false;

        Marshal.WriteInt64(buffer, BufferPtsOffset, frame.Timestamp >= 0 ? frame.Timestamp : unchecked((long)ClockTimeNone));

        if (!frame.IsKeyFrame)
            Marshal.WriteInt32(buffer, MiniObjectFlagsOffset, Marshal.ReadInt32(buffer, MiniObjectFlagsOffset) | DeltaUnitFlag);

        // The appsrc takes the buffer over, whatever it answers
        return gst_app_src_push_buffer(appsrc.Handle, buffer) == (int)FlowReturn.Ok;
    }

    /// <summary>
    /// Sends a keyframe request upstream from <paramref name="element"/>: to the encoder from the end
    /// of a capture, to the RTP session (which asks the sender with a PLI) from the end of a receiving
    /// branch.
    /// </summary>
    public static bool RequestKeyFrame(Element element)
    {
        var request = gst_video_event_new_upstream_force_key_unit(ClockTimeNone, 1, 0);
        return request != IntPtr.Zero && gst_element_send_event(element.Handle, request) != 0;
    }

    /// <summary>
    /// Calls <paramref name="onRequest"/> for every keyframe request that reaches <paramref name="pad"/>
    /// from downstream, for as long as the pad lives. The call comes on the thread that sent the request.
    /// </summary>
    public static void WatchKeyFrameRequests(Pad pad, Action onRequest)
    {
        var handle = GCHandle.Alloc(onRequest);
        gst_pad_add_probe(pad.Handle, ProbeEventUpstream, &OnUpstreamEvent, GCHandle.ToIntPtr(handle), &FreeHandle);
    }

    [UnmanagedCallersOnly]
    private static int OnUpstreamEvent(IntPtr pad, IntPtr info, IntPtr data)
    {
        var @event = gst_pad_probe_info_get_event(info);

        if (@event == IntPtr.Zero || gst_video_event_is_force_key_unit(@event) == 0)
            return ProbeOk;

        try
        {
            ((Action)GCHandle.FromIntPtr(data).Target!)();
        }
        catch (Exception)
        {
            // Nothing may leave a streaming thread; the next request tries again
        }

        return ProbeOk;
    }

    /// <summary>
    /// Makes the keyframe requests that leave upstream through <paramref name="pad"/> plain ones, without
    /// the parameter sets. The RTP session asks the sender with a PLI for a plain request only: one with
    /// the parameter sets, which the depayloaders send on a loss, becomes a FIR that never reaches the
    /// sender, and the picture stays broken. The sender repeats the parameter sets before every
    /// keyframe anyway.
    /// </summary>
    public static void RequestKeyFramesWithPli(Pad pad) =>
        gst_pad_add_probe(pad.Handle, ProbeEventUpstream, &OnKeyFrameRequestLeaving, IntPtr.Zero, null);

    [UnmanagedCallersOnly]
    private static int OnKeyFrameRequestLeaving(IntPtr pad, IntPtr info, IntPtr data)
    {
        var @event = gst_pad_probe_info_get_event(info);

        if (@event == IntPtr.Zero || gst_video_event_is_force_key_unit(@event) == 0 ||
            gst_video_event_parse_upstream_force_key_unit(@event, out var runningTime, out var allHeaders, out var count) == 0 ||
            allHeaders == 0)
            return ProbeOk;

        // The plain request passes this probe on its way out
        gst_pad_push_event(pad, gst_video_event_new_upstream_force_key_unit(runningTime, 0, count));
        return ProbeDrop;
    }

    [UnmanagedCallersOnly]
    private static void FreeHandle(IntPtr data) => GCHandle.FromIntPtr(data).Free();
}
