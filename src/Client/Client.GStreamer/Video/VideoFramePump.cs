using Client.Core.Media;
using Gst;
using Client.GStreamer.Interop;

namespace Client.GStreamer.Video;

/// <summary>
/// Hands the frames that arrive at an appsink to a callback as <see cref="RawVideoFrame"/>.
/// </summary>
internal sealed class VideoFramePump : IDisposable
{
    /// <summary>
    /// What the sink has to be fed with for <see cref="RawVideoFrame"/> to describe it in memory.
    /// </summary>
    public const string FrameCaps = "video/x-raw,format=BGRA";

    private readonly Action<RawVideoFrame> _onFrame;
    private readonly Action<VideoOutputKind> _onSurfaceFailed;
    private readonly VideoOutputKind _kind;
    private readonly AppSinkReader _reader;

    // What the caps of the stream say, read again only when they change
    private IntPtr _capsHandle;
    private VideoCapsInfo _capsInfo;

    // Writes Direct3D 11 frames into textures the compositor can open, made on the first frame
    private D3D11SharedTextureWriter? _d3d11Writer;

    /// <param name="sink">An appsink parsed from <see cref="SinkDescription"/>, already inside its bin.</param>
    /// <param name="kind">The form the frames arrive in, as the branch was built for.</param>
    /// <param name="name">Names the thread the frames are delivered on.</param>
    /// <param name="onFrame">
    /// Called on that thread. The frame is only valid during the call, so the receiver copies the
    /// pixels or retains the GPU frame it keeps.
    /// </param>
    /// <param name="onSurfaceFailed">
    /// Called when frames of a GPU kind cannot be delivered, after which none of that kind come.
    /// </param>
    public VideoFramePump(Element sink, VideoOutputKind kind, string name, Action<RawVideoFrame> onFrame,
        Action<Exception> onError, Action<VideoOutputKind>? onSurfaceFailed = null)
    {
        _kind = kind;
        _onFrame = onFrame;

        // The plane layout of GPU frames is only in their video meta; planes in memory may have one
        // too, which saves the decoder from copying them into the default layout
        if (kind != VideoOutputKind.Memory)
            GstVideoNative.AcceptVideoMeta(sink);

        _onSurfaceFailed = onSurfaceFailed ?? (_ => { });
        _reader = new AppSinkReader(sink, name, Deliver, onError);
    }

    /// <summary>
    /// A sink that never waits for the clock: frames are placed in time where they are shown.
    /// GPU frames are held by the screen until the next one replaces them, so the sink keeps
    /// one more than it hands out.
    /// </summary>
    public static string SinkDescription(string name) =>
        $"appsink name={name} max-buffers=2 drop=true wait-on-eos=false sync=false async=false";

    private void Deliver(Sample sample)
    {
        using var caps = sample.GetCaps();

        if (caps == null || caps.GetSize() == 0)
            return;

        if (caps.Handle != _capsHandle)
        {
            _capsInfo = VideoCapsInfo.Read(caps);
            _capsHandle = caps.Handle;
        }

        if (_capsInfo.Width <= 0 || _capsInfo.Height <= 0)
            return;

        using var buffer = sample.GetBuffer();

        if (buffer == null)
            return;

        var timestamp = GstVideoNative.GetPts(buffer.Handle);

        switch (_kind)
        {
            case VideoOutputKind.DmaBuf:
                DeliverDmaBuf(sample, buffer, timestamp);
                break;
            case VideoOutputKind.Cuda:
                DeliverCuda(sample, buffer, timestamp);
                break;
            case VideoOutputKind.D3D11:
                DeliverD3D11(buffer, timestamp);
                break;
            case VideoOutputKind.MemoryYuv:
                DeliverMemoryYuv(sample, buffer, timestamp);
                break;
            default:
                DeliverMemory(buffer, timestamp);
                break;
        }
    }

    private void DeliverMemory(Gst.Buffer buffer, long timestamp)
    {
        if (!buffer.Map(out var map, MapFlags.Read))
            return;

        try
        {
            var (width, height) = (_capsInfo.Width, _capsInfo.Height);

            // BGRA rows carry no padding
            var stride = width * 4;

            if ((long)map.Size >= (long)stride * height)
                _onFrame(new RawVideoFrame(map.DataPtr, width, height, stride) { Timestamp = timestamp });
        }
        finally
        {
            buffer.Unmap(map);
        }
    }

    private void DeliverMemoryYuv(Sample sample, Gst.Buffer buffer, long timestamp)
    {
        var frame = GstMemoryYuvFrame.TryCreate(sample, buffer.Handle, _capsInfo);

        if (frame == null)
        {
            _onSurfaceFailed(_kind);
            return;
        }

        Hand(frame, timestamp);
    }

    private void DeliverDmaBuf(Sample sample, Gst.Buffer buffer, long timestamp)
    {
        var frame = GstDmaBufFrame.TryCreate(sample, buffer.Handle, _capsInfo);

        if (frame == null)
        {
            _onSurfaceFailed(_kind);
            return;
        }

        Hand(frame, timestamp);
    }

    private void DeliverCuda(Sample sample, Gst.Buffer buffer, long timestamp)
    {
        var frame = CudaGlFrame.TryCreate(sample, buffer.Handle, _capsInfo);

        if (frame == null)
        {
            _onSurfaceFailed(_kind);
            return;
        }

        Hand(frame, timestamp);
    }

    private void DeliverD3D11(Gst.Buffer buffer, long timestamp)
    {
        _d3d11Writer ??= new D3D11SharedTextureWriter();

        var frame = _d3d11Writer.Write(buffer.Handle, _capsInfo);

        if (frame == null)
        {
            if (_d3d11Writer.IsBroken)
                _onSurfaceFailed(_kind);

            return;
        }

        Hand(frame, timestamp);
    }

    // The callback retains what it keeps; the reference of the pump goes back right after
    private void Hand(GpuVideoFrame frame, long timestamp)
    {
        try
        {
            _onFrame(new RawVideoFrame(IntPtr.Zero, frame.Width, frame.Height, 0) { Timestamp = timestamp, Gpu = frame });
        }
        finally
        {
            frame.Dispose();
        }
    }

    /// <summary>
    /// Stops the delivery and waits for a frame that is being delivered. Call before the sink is shut down.
    /// </summary>
    public void Dispose()
    {
        _reader.Dispose();
        _d3d11Writer?.Dispose();
    }
}

/// <summary>
/// What the caps of a video stream say about its frames.
/// </summary>
internal readonly record struct VideoCapsInfo(int Width, int Height, string? Format, uint DrmFourcc,
    ulong DrmModifier, VideoColorMatrix Matrix, bool IsFullRange)
{
    public static VideoCapsInfo Read(Caps caps)
    {
        using var structure = caps.GetStructure(0);

        structure.GetInt("width", out var width);
        structure.GetInt("height", out var height);

        var (fourcc, modifier) = ParseDrmFormat(structure.GetString("drm-format"));
        var (matrix, isFullRange) = ParseColorimetry(structure.GetString("colorimetry"));

        return new VideoCapsInfo(width, height, structure.GetString("format"), fourcc, modifier, matrix, isFullRange);
    }

    // "NV12:0x0200000000000004", or "NV12" for a linear layout
    private static (uint Fourcc, ulong Modifier) ParseDrmFormat(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return (0, 0);

        var parts = value.Split(':');
        var name = parts[0].PadRight(4)[..4];
        var fourcc = (uint)name[0] | (uint)name[1] << 8 | (uint)name[2] << 16 | (uint)name[3] << 24;
        ulong modifier = 0;

        if (parts.Length > 1)
        {
            var text = parts[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? parts[1][2..] : parts[1];
            ulong.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out modifier);
        }

        return (fourcc, modifier);
    }

    // A name such as "bt709", or "range:matrix:transfer:primaries" with GstVideoColorRange 1 for full
    // and GstVideoColorMatrix 4 or 5 for BT.601
    private static (VideoColorMatrix, bool) ParseColorimetry(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return (VideoColorMatrix.Bt709, false);

        var parts = value.Split(':');

        if (parts.Length >= 2)
            return (parts[1] is "4" or "5" ? VideoColorMatrix.Bt601 : VideoColorMatrix.Bt709, parts[0] == "1");

        return value switch
        {
            "bt601" or "smpte170m" => (VideoColorMatrix.Bt601, false),
            "jpeg" => (VideoColorMatrix.Bt601, true),
            "sRGB" => (VideoColorMatrix.Bt709, true),
            _ => (VideoColorMatrix.Bt709, false)
        };
    }
}
