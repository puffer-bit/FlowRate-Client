using Client.Core.Media;
using Client.GStreamer.Interop;
using Gst;

namespace Client.GStreamer.Video;

/// <summary>
/// A DMA-BUF frame of a VA-API decoder. It holds the sample, so the decoder does not reuse the
/// buffer while the screen still reads it.
/// </summary>
internal sealed class GstDmaBufFrame : DmaBufVideoFrame
{
    private IntPtr _sample;

    private GstDmaBufFrame(IntPtr sample, VideoCapsInfo caps, IReadOnlyList<DmaBufPlane> planes)
        : base(caps.Width, caps.Height, caps.DrmFourcc, caps.DrmModifier, planes)
    {
        _sample = sample;
    }

    /// <returns>Null when the buffer has no supported RGB or YUV DMA-BUF layout.</returns>
    public static GstDmaBufFrame? TryCreate(Sample sample, IntPtr buffer, VideoCapsInfo caps)
    {
        var isRgb = IsRgbFormat(caps.DrmFourcc);
        if (!isRgb && caps.DrmFourcc is not (FourccNv12 or FourccP010))
            return null;

        var layout = GstVideoNative.GetPlaneLayout(buffer);

        if (layout == null || layout.Length != (isRgb ? 1 : 2))
            return null;

        var planes = new DmaBufPlane[layout.Length];

        for (var plane = 0; plane < layout.Length; plane++)
        {
            var place = GstVideoNative.FindDmaBuf(buffer, layout[plane].Offset);

            if (place == null || layout[plane].Stride <= 0)
                return null;

            planes[plane] = new DmaBufPlane(place.Value.Fd, (uint)place.Value.Offset, (uint)layout[plane].Stride);
        }

        return new GstDmaBufFrame(GstVideoNative.Ref(sample.Handle), caps, planes)
        {
            Matrix = caps.Matrix,
            IsFullRange = caps.IsFullRange
        };
    }

    protected override void Release()
    {
        var sample = Interlocked.Exchange(ref _sample, IntPtr.Zero);

        if (sample != IntPtr.Zero)
            GstVideoNative.Unref(sample);
    }
}
