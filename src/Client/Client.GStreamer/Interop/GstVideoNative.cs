using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Client.GStreamer.Interop;

/// <summary>
/// Reads of a video buffer the binding has no wrapper for: its timestamp, the layout of its
/// planes and the DMA-BUF descriptors of its memories.
/// </summary>
internal static class GstVideoNative
{
    [DllImport(NativeLibraries.GstVideo)]
    private static extern IntPtr gst_buffer_get_video_meta(IntPtr buffer);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern uint gst_buffer_n_memory(IntPtr buffer);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_buffer_peek_memory(IntPtr buffer, uint index);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern int gst_buffer_find_memory(IntPtr buffer, nuint offset, nuint size,
        out uint index, out uint length, out nuint skip);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_mini_object_ref(IntPtr miniObject);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_mini_object_unref(IntPtr miniObject);

    [DllImport(NativeLibraries.GstAllocators)]
    private static extern int gst_is_dmabuf_memory(IntPtr memory);

    [DllImport(NativeLibraries.GstAllocators)]
    private static extern int gst_dmabuf_memory_get_fd(IntPtr memory);

    // GstBuffer on a 64-bit system: the mini object (64 bytes) and the pool (8) come before pts
    private const int BufferPtsOffset = 72;

    // GstMemory: the mini object, allocator, parent, maxsize and align come before offset
    private const int MemoryOffsetOffset = 96;

    // GstVideoMeta: GstMeta (16), buffer, flags, format, id, width, height, n_planes, then
    // gsize offset[4] and gint stride[4]
    private const int VideoMetaPlanesOffset = 44;
    private const int VideoMetaOffsetsOffset = 48;
    private const int VideoMetaStridesOffset = 80;

    private const ulong ClockTimeNone = ulong.MaxValue;

    /// <summary>The presentation timestamp of a buffer in nanoseconds, or -1 when it has none.</summary>
    public static long GetPts(IntPtr buffer)
    {
        var pts = (ulong)Marshal.ReadInt64(buffer, BufferPtsOffset);

        return pts == ClockTimeNone || pts > long.MaxValue ? -1 : (long)pts;
    }

    /// <summary>
    /// The offset and the stride of every plane, as the producer of the buffer set them.
    /// </summary>
    /// <returns>Null when the buffer carries no video meta.</returns>
    public static (long Offset, int Stride)[]? GetPlaneLayout(IntPtr buffer)
    {
        var meta = gst_buffer_get_video_meta(buffer);

        if (meta == IntPtr.Zero)
            return null;

        var count = Math.Clamp(Marshal.ReadInt32(meta, VideoMetaPlanesOffset), 0, 4);
        var planes = new (long, int)[count];

        for (var plane = 0; plane < count; plane++)
            planes[plane] = (Marshal.ReadInt64(meta, VideoMetaOffsetsOffset + plane * 8),
                Marshal.ReadInt32(meta, VideoMetaStridesOffset + plane * 4));

        return planes;
    }

    /// <summary>
    /// The DMA-BUF descriptor holding the byte at <paramref name="offset"/> of the buffer, and
    /// where that byte is inside it.
    /// </summary>
    /// <returns>Null when the byte is not in a DMA-BUF memory.</returns>
    public static (int Fd, long Offset)? FindDmaBuf(IntPtr buffer, long offset)
    {
        if (gst_buffer_find_memory(buffer, (nuint)offset, 1, out var index, out _, out var skip) == 0)
            return null;

        var memory = gst_buffer_peek_memory(buffer, index);

        if (memory == IntPtr.Zero || gst_is_dmabuf_memory(memory) == 0)
            return null;

        var fd = gst_dmabuf_memory_get_fd(memory);

        if (fd < 0)
            return null;

        return (fd, Marshal.ReadInt64(memory, MemoryOffsetOffset) + (long)skip);
    }

    public static uint GetMemoryCount(IntPtr buffer) => gst_buffer_n_memory(buffer);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_element_get_static_pad(IntPtr element, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern unsafe ulong gst_pad_add_probe(IntPtr pad, int mask,
        delegate* unmanaged<IntPtr, IntPtr, IntPtr, int> callback, IntPtr data, IntPtr destroy);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_pad_probe_info_get_query(IntPtr info);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_object_unref(IntPtr instance);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_query_add_allocation_meta(IntPtr query, IntPtr api, IntPtr parameters);

    [DllImport(NativeLibraries.GstVideo)]
    private static extern IntPtr gst_video_meta_api_get_type();

    // A query that goes downstream, seen before the pad answers it; HANDLED answers it in its place
    private const int ProbeQueryDownstream = 1 << 9;
    private const int ProbePush = 1 << 12;
    private const int ProbeOk = 1;
    private const int ProbeHandled = 4;

    // GST_QUERY_ALLOCATION, and where GstQuery keeps its type: right after the mini object
    // (number << 8) | flags, the flags being downstream (2) and serialized (4)
    private const int QueryAllocation = (140 << 8) | 2 | 4;
    private const int QueryTypeOffset = 64;

    /// <summary>
    /// Makes an appsink tell upstream it reads the plane layout from the video meta. A decoder that
    /// hands out DMA-BUF frames refuses to negotiate without it ("DMABuf caps negotiated without the
    /// mandatory support of VideoMeta").
    /// </summary>
    /// <returns>False when the sink has no sink pad.</returns>
    public static unsafe bool AcceptVideoMeta(Gst.Element appsink)
    {
        var pad = gst_element_get_static_pad(appsink.Handle, "sink");

        if (pad == IntPtr.Zero)
            return false;

        gst_pad_add_probe(pad, ProbeQueryDownstream | ProbePush, &OnQuery, IntPtr.Zero, IntPtr.Zero);
        gst_object_unref(pad);
        return true;
    }

    // Runs on a streaming thread: no wrappers, only the native calls. appsink itself refuses the
    // allocation query, so the probe answers it: no pool, only the meta
    [UnmanagedCallersOnly]
    private static int OnQuery(IntPtr pad, IntPtr info, IntPtr data)
    {
        var query = gst_pad_probe_info_get_query(info);

        if (query == IntPtr.Zero || Marshal.ReadInt32(query, QueryTypeOffset) != QueryAllocation)
            return ProbeOk;

        gst_query_add_allocation_meta(query, gst_video_meta_api_get_type(), IntPtr.Zero);
        return ProbeHandled;
    }

    /// <summary>Takes a reference to a sample, buffer or caps.</summary>
    public static IntPtr Ref(IntPtr miniObject) => gst_mini_object_ref(miniObject);

    public static void Unref(IntPtr miniObject) => gst_mini_object_unref(miniObject);
}
