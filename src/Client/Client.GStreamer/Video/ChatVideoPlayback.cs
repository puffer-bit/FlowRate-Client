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

    public ChatVideoPlayback(string path, Action<RawVideoFrame> onFrame, double volume = 1, bool preview = false)
    {
        _pipeline = (Pipeline)Global.ParseLaunch("playbin");
        using var output = Global.ParseBinFromDescription(
            (preview ? "videoconvert ! videoscale ! video/x-raw,format=BGRA,width=400,height=225,pixel-aspect-ratio=1/1 ! "
                : "videoconvert ! video/x-raw,format=BGRA ! ") +
            "appsink name=frames max-buffers=2 drop=true wait-on-eos=false sync=true", true);
        _pipeline.SetProperty("video-sink", output);
        if (preview)
        {
            using var silentOutput = Global.ParseLaunch("fakesink sync=false");
            _pipeline.SetProperty("audio-sink", silentOutput);
        }
        // The pump owns the sink wrapper until it stops.
        var sink = output.GetByName("frames")!;
        _pump = new VideoFramePump(sink, VideoOutputKind.Memory, "chat_video", onFrame,
            exception => Interlocked.Exchange(ref _frameError, exception));
        _bus = _pipeline.GetBus();
        try
        {
            _pipeline.SetProperty("uri", new System.Uri(Path.GetFullPath(path)).AbsoluteUri);
            SetVolume(volume);
            SetPlaying(true);
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
