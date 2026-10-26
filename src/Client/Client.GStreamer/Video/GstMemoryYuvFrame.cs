using System.Runtime.InteropServices;
using Client.Core.Media;
using Client.GStreamer.Interop;
using Gst;

namespace Client.GStreamer.Video;

/// <summary>
/// An NV12 or I420 frame in memory. It holds the sample and keeps the buffer mapped until it is
/// released, so the screen reads the planes where the decoder wrote them.
/// </summary>
internal sealed class GstMemoryYuvFrame : MemoryYuvVideoFrame
{
    private const int MapRead = 1;

    // GstMapInfo: memory, flags with padding, data, size, maxsize, then user data and padding
    private const int MapInfoSize = 128;
    private const int MapInfoDataOffset = 16;
    private const int MapInfoSizeOffset = 24;

    private IntPtr _sample;
    private readonly IntPtr _buffer;
    private readonly IntPtr _mapInfo;

    private GstMemoryYuvFrame(IntPtr sample, IntPtr buffer, IntPtr mapInfo, VideoCapsInfo caps, YuvPlaneLayout layout,
        IReadOnlyList<VideoPlane> planes)
        : base(caps.Width, caps.Height, layout, planes)
    {
        _sample = sample;
        _buffer = buffer;
        _mapInfo = mapInfo;
    }

    /// <returns>Null when the format is not NV12 or I420, or the buffer cannot hold the planes.</returns>
    public static GstMemoryYuvFrame? TryCreate(Sample sample, IntPtr buffer, VideoCapsInfo caps)
    {
        YuvPlaneLayout layout;

        switch (caps.Format)
        {
            case "NV12":
                layout = YuvPlaneLayout.Nv12;
                break;
            case "I420":
                layout = YuvPlaneLayout.I420;
                break;
            default:
                return null;
        }

        var places = GstVideoNative.GetPlaneLayout(buffer) ?? DefaultLayout(layout, caps.Width, caps.Height);

        if (places.Length != (layout == YuvPlaneLayout.Nv12 ? 2 : 3))
            return null;

        var mapInfo = Marshal.AllocHGlobal(MapInfoSize);

        if (gst_buffer_map(buffer, mapInfo, MapRead) == 0)
        {
            Marshal.FreeHGlobal(mapInfo);
            return null;
        }

        var data = Marshal.ReadIntPtr(mapInfo, MapInfoDataOffset);
        var size = Marshal.ReadInt64(mapInfo, MapInfoSizeOffset);
        var planes = new VideoPlane[places.Length];

        for (var plane = 0; plane < places.Length; plane++)
        {
            var (offset, stride) = places[plane];

            // Chroma has half the rows, and a row of NV12 chroma is as wide as a row of luma
            var rows = plane == 0 ? caps.Height : (caps.Height + 1) / 2;
            var rowBytes = plane == 0 || layout == YuvPlaneLayout.Nv12 ? caps.Width : (caps.Width + 1) / 2;

            if (stride < rowBytes || offset < 0 || offset + (long)stride * (rows - 1) + rowBytes > size)
            {
                gst_buffer_unmap(buffer, mapInfo);
                Marshal.FreeHGlobal(mapInfo);
                return null;
            }

            planes[plane] = new VideoPlane(data + (nint)offset, stride);
        }

        return new GstMemoryYuvFrame(GstVideoNative.Ref(sample.Handle), buffer, mapInfo, caps, layout, planes)
        {
            Matrix = caps.Matrix,
            IsFullRange = caps.IsFullRange
        };
    }

    // Where the planes are without a video meta, as gst_video_info_set_format lays them out
    private static (long Offset, int Stride)[] DefaultLayout(YuvPlaneLayout layout, int width, int height)
    {
        var lumaStride = RoundUp(width, 4);
        var chromaOffset = (long)lumaStride * RoundUp(height, 2);

        if (layout == YuvPlaneLayout.Nv12)
            return [(0, lumaStride), (chromaOffset, lumaStride)];

        var chromaStride = RoundUp(RoundUp(width, 2) / 2, 4);
        return [(0, lumaStride), (chromaOffset, chromaStride),
            (chromaOffset + (long)chromaStride * (RoundUp(height, 2) / 2), chromaStride)];
    }

    private static int RoundUp(int value, int multiple) => (value + multiple - 1) / multiple * multiple;

    protected override void Release()
    {
        var sample = Interlocked.Exchange(ref _sample, IntPtr.Zero);

        if (sample == IntPtr.Zero)
            return;

        // The sample owns the buffer, so it goes last
        gst_buffer_unmap(_buffer, _mapInfo);
        Marshal.FreeHGlobal(_mapInfo);
        GstVideoNative.Unref(sample);
    }

    [DllImport(NativeLibraries.GStreamer)]
    private static extern int gst_buffer_map(IntPtr buffer, IntPtr info, int flags);

    [DllImport(NativeLibraries.GStreamer)]
    private static extern void gst_buffer_unmap(IntPtr buffer, IntPtr info);
}
