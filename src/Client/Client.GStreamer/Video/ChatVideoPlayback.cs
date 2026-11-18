using Gst;

using Client.Core.Media;

namespace Client.GStreamer.Video;

/// <summary>A local video pipeline, accessed exclusively from its owning UI thread.</summary>
public sealed class ChatVideoPlayback : IDisposable
{
    private readonly Pipeline _pipeline;
    private readonly Bus _bus;
    public bool IsPlaying { get; private set; }
    public bool HasEnded { get; private set; }
    public double Position { get; private set; }
    public double Duration { get; private set; }
    public double Volume { get; private set; }

    private readonly VideoFramePump _pump;
    private bool _disposed;

    // A position to go to once the stream knows its duration, for a pipeline rebuilt mid-playback
    private double _pendingSeek;

    /// <summary>
    /// The frames are YUV planes as decoded, uploaded and converted by the OpenGL presenter;
    /// otherwise BGRA converted on the CPU.
    /// </summary>
    public bool DeliversYuv { get; }

    /// <param name="startAt">Seconds to start from; the picture starts paused there unless <paramref name="play"/>.</param>
    public ChatVideoPlayback(string path, Action<RawVideoFrame> onFrame, double volume = 1, double startAt = 0,
        bool play = true)
    {
        DeliversYuv = VideoSurfaceSupport.Supports(VideoSurfaceKinds.YuvMemory);
        _pipeline = (Pipeline)Global.ParseLaunch("playbin");
        // Decoders output NV12 or I420 anyway, so videoconvert passes them through and only converts
        // other formats (10-bit, 4:4:4). qos lets a decoder that falls behind skip late frames.
        var caps = DeliversYuv ? "video/x-raw,format=(string){NV12,I420}" : VideoFramePump.FrameCaps;
        using var output = Global.ParseBinFromDescription(
            $"videoconvert n-threads=0 ! {caps} ! " +
            "appsink name=frames max-buffers=1 drop=true enable-last-sample=false wait-on-eos=false sync=true qos=true",
            true);
        _pipeline.SetProperty("video-sink", output);
        // The pump owns the sink wrapper until it stops.
        var sink = output.GetByName("frames")!;
        _pump = new VideoFramePump(sink, DeliversYuv ? VideoOutputKind.MemoryYuv : VideoOutputKind.Memory, "chat_video",
            onFrame, exception => Interlocked.Exchange(ref _frameError, exception));
        _bus = _pipeline.GetBus();
        try
        {
            _pipeline.SetProperty("uri", new System.Uri(Path.GetFullPath(path)).AbsoluteUri);
            SetVolume(volume);
            _pendingSeek = startAt;
            Position = startAt;
            SetPlaying(play);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void SetVolume(double volume)
    {
        volume = Math.Clamp(volume, 0, 1);
        _pipeline.SetProperty("volume", volume);
        Volume = volume;
    }

    public void SetPlaying(bool playing)
    {
        if (playing && HasEnded)
        {
            Seek(0);
            HasEnded = false;
        }
        if (_pipeline.SetState(playing ? State.Playing : State.Paused) == StateChangeReturn.Failure)
            throw new InvalidOperationException("Video pipeline could not change state.");
        IsPlaying = playing;
    }

    public void Seek(double seconds)
    {
        seconds = Math.Clamp(seconds, 0, Duration);
        if (!_pipeline.SeekSimple(Format.Time, SeekFlags.Flush | SeekFlags.Accurate,
                (long)(seconds * 1_000_000_000)))
            return;
        HasEnded = false;
        Position = seconds;
    }

    private Exception? _frameError;

    public void Update()
    {
        if (Interlocked.Exchange(ref _frameError, null) is { } error)
            throw new InvalidOperationException("Video frame delivery failed.", error);
        using var message = _bus.PopFiltered(MessageType.Error | MessageType.Eos);
        if (message?.Type == MessageType.Error)
        {
            var (gstError, debug) = message.ParseError();
            throw new InvalidOperationException($"Video playback failed: {gstError.Message} ({debug})");
        }
        if (_pipeline.QueryDuration(Format.Time, out var duration) && duration > 0)
            Duration = duration / 1_000_000_000d;
        if (_pendingSeek > 0 && Duration > 0)
        {
            var target = _pendingSeek;
            _pendingSeek = 0;
            Seek(target);
            return;
        }
        if (_pendingSeek > 0)
            return;
        if (_pipeline.QueryPosition(Format.Time, out var position) && position >= 0)
            Position = position / 1_000_000_000d;
        if (message?.Type == MessageType.Eos)
        {
            _pipeline.SetState(State.Paused);
            IsPlaying = false;
            HasEnded = true;
            Position = Duration;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pump.Dispose();
        _pipeline.SetState(State.Null);
        _bus.Dispose();
        _pipeline.Dispose();
    }
}
