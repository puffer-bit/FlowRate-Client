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
    private static extern IntPtr gst_buffer_copy(IntPtr buffer);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern int gst_buffer_map(IntPtr buffer, out MapInfo info, MapFlags flags);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_buffer_unmap(IntPtr buffer, ref MapInfo info);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern int gst_element_send_event(IntPtr element, IntPtr @event);

    [DllImport(NativeLibraries.GstApp)]
    private static extern int gst_app_src_push_buffer(IntPtr appsrc, IntPtr buffer);

    [DllImport(NativeLibraries.GstVideo)]
    private static extern IntPtr gst_video_event_new_upstream_force_key_unit(ulong runningTime, int allHeaders, uint count);

    // GstBuffer: the mini object (64 bytes), the pool, then pts; the flags of the mini object follow
    // its type, ref count and lock state
    private const int BufferPtsOffset = 72;
    private const int MiniObjectFlagsOffset = 16;

    // GST_BUFFER_FLAG_DELTA_UNIT, GST_MINI_OBJECT_FLAG_LAST << 9
    private const int DeltaUnitFlag = 1 << 13;

    private const ulong ClockTimeNone = ulong.MaxValue;

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
                pts == ClockTimeNone || pts > long.MaxValue ? -1 : (long)pts, isKeyFrame)
            {
                NativeBuffer = buffer
            });
            return true;
        }
        finally
        {
            gst_buffer_unmap(handle, ref map);
        }
    }

    /// <summary>
    /// Shares a native frame's storage, or copies foreign bytes, into an appsrc. The timestamp becomes the PTS unless the
    /// appsrc stamps the buffers itself (do-timestamp) or the frame has none.
    /// </summary>
    /// <returns>False when the appsrc refused it, as it does while it is not running.</returns>
    public static bool Push(Element appsrc, EncodedVideoFrame frame)
    {
        // Copy only the header of a borrowed GstBuffer. Its memory is referenced, while timestamps
        // remain independent of the source pipeline. Non-shareable memory is copied by GStreamer.
        var buffer = frame.NativeBuffer is Gst.Buffer native
            ? gst_buffer_copy(native.Handle)
            : gst_buffer_new_memdup(frame.Data, (nuint)frame.Length);

        if (buffer == IntPtr.Zero)
            return false;

        Marshal.WriteInt64(buffer, BufferPtsOffset, frame.Timestamp >= 0 ? frame.Timestamp : unchecked((long)ClockTimeNone));
        // A different pipeline uses a different clock; do not carry a stale DTS across the bridge.
        Marshal.WriteInt64(buffer, BufferPtsOffset + sizeof(long), unchecked((long)ClockTimeNone));

        if (!frame.IsKeyFrame)
            Marshal.WriteInt32(buffer, MiniObjectFlagsOffset, Marshal.ReadInt32(buffer, MiniObjectFlagsOffset) | DeltaUnitFlag);
        else
            Marshal.WriteInt32(buffer, MiniObjectFlagsOffset, Marshal.ReadInt32(buffer, MiniObjectFlagsOffset) & ~DeltaUnitFlag);

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
}
