using System.Runtime.InteropServices;
using Gst;

namespace Client.GStreamer.Interop;

/// <summary>Replaces packet payloads while preserving buffer metadata and explicit native ownership.</summary>
public static unsafe class GstPacketTransform
{
    public delegate byte[] Transform(ReadOnlySpan<byte> packet);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern ulong gst_pad_add_probe(IntPtr pad, int mask,
        delegate* unmanaged<IntPtr, IntPtr, IntPtr, int> callback, IntPtr data,
        delegate* unmanaged<IntPtr, void> destroy);
    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_pad_probe_info_get_buffer(IntPtr info);
    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_pad_probe_info_set_buffer(IntPtr info, IntPtr buffer);
    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_pad_probe_info_get_buffer_list(IntPtr info);
    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_pad_probe_info_set_buffer_list(IntPtr info, IntPtr list);
    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_buffer_list_new();
    [DllImport(NativeLibraries.GStreamer)]
    private static extern uint gst_buffer_list_length(IntPtr list);
    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_buffer_list_get(IntPtr list, uint index);
    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_buffer_list_insert(IntPtr list, int index, IntPtr buffer);
    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_mini_object_unref(IntPtr instance);
    [DllImport(NativeLibraries.GStreamer)]
    private static extern int gst_buffer_map(IntPtr buffer, out MapInfo info, MapFlags flags);
    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_buffer_unmap(IntPtr buffer, ref MapInfo info);
    [DllImport(NativeLibraries.GStreamer)]
    private static extern IntPtr gst_buffer_new_memdup(IntPtr data, nuint size);
    [DllImport(NativeLibraries.GStreamer)]
    private static extern int gst_buffer_copy_into(IntPtr destination, IntPtr source, BufferCopyFlags flags, nuint offset, nuint size);

    /// <summary>The pad owns the transform until its probe is destroyed. Supports buffers and buffer lists.</summary>
    public static bool Attach(Element element, string padName, Transform transform)
    {
        using var pad = element.GetStaticPad(padName);
        if (pad == null)
            return false;
        var handle = GCHandle.Alloc(transform);
        var probe = gst_pad_add_probe(pad.Handle, (int)(PadProbeType.Buffer | PadProbeType.BufferList),
            &OnPacket, GCHandle.ToIntPtr(handle), &Free);
        if (probe == 0)
            handle.Free();
        return probe != 0;
    }

    [UnmanagedCallersOnly]
    private static int OnPacket(IntPtr pad, IntPtr info, IntPtr data)
    {
        try
        {
            var transform = (Transform)GCHandle.FromIntPtr(data).Target!;
            if (((PadProbeType)Marshal.ReadInt32(info) & PadProbeType.BufferList) != 0)
            {
                var input = gst_pad_probe_info_get_buffer_list(info);
                var output = gst_buffer_list_new();
                try
                {
                    for (uint index = 0; index < gst_buffer_list_length(input); index++)
                        gst_buffer_list_insert(output, -1, Rewrite(gst_buffer_list_get(input, index), transform));
                    gst_pad_probe_info_set_buffer_list(info, output);
                    output = IntPtr.Zero; // The probe consumes this reference.
                }
                finally
                {
                    if (output != IntPtr.Zero) gst_mini_object_unref(output);
                }
            }
            else
                gst_pad_probe_info_set_buffer(info, Rewrite(gst_pad_probe_info_get_buffer(info), transform));
            return (int)PadProbeReturn.Ok;
        }
        catch (Exception)
        {
            // Never propagate into a streaming thread or send a partially transformed packet list.
            return (int)PadProbeReturn.Drop;
        }
    }

    private static IntPtr Rewrite(IntPtr input, Transform transform)
    {
        if (gst_buffer_map(input, out var map, MapFlags.Read) == 0)
            throw new InvalidOperationException("Cannot map RTP packet");
        try
        {
            var bytes = transform(new ReadOnlySpan<byte>((void*)map.DataPtr, checked((int)map.Size)));
            fixed (byte* pointer = bytes)
            {
                var output = gst_buffer_new_memdup((IntPtr)pointer, (nuint)bytes.Length);
                if (output == IntPtr.Zero) throw new OutOfMemoryException();
                if (gst_buffer_copy_into(output, input,
                        BufferCopyFlags.Flags | BufferCopyFlags.Timestamps | BufferCopyFlags.Meta, 0, nuint.MaxValue) != 0)
                    return output;
                gst_mini_object_unref(output);
                throw new InvalidOperationException("Cannot copy RTP metadata");
            }
        }
        finally
        {
            gst_buffer_unmap(input, ref map);
        }
    }

    [UnmanagedCallersOnly]
    private static void Free(IntPtr data) => GCHandle.FromIntPtr(data).Free();
}
