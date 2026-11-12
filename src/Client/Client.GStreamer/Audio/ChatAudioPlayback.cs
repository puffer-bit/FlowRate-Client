using Gst;

namespace Client.GStreamer.Audio;

/// <summary>A local audio pipeline, accessed exclusively from its owning UI thread.</summary>
public sealed class ChatAudioPlayback : IDisposable
{
    private readonly Pipeline _pipeline;
    private readonly Bus _bus;
    public bool IsPlaying { get; private set; }
    public bool HasEnded { get; private set; }
    public double Position { get; private set; }
    public double Duration { get; private set; }
    public double Volume { get; private set; }

    public ChatAudioPlayback(string path, double volume = 1) : this(path, "autoaudiosink", volume)
    {
    }

    internal ChatAudioPlayback(string path, string output, double volume = 1)
    {
        _pipeline = (Pipeline)Global.ParseLaunch(
            "filesrc name=file ! decodebin ! audioconvert ! audioresample ! volume name=level ! " + output);
        _bus = _pipeline.GetBus();
        try
        {
            using var source = _pipeline.GetByName("file");
            source!.SetProperty("location", Path.GetFullPath(path));
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
        using var level = _pipeline.GetByName("level");
        level!.SetProperty("volume", volume);
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
            throw new InvalidOperationException("Audio pipeline could not change state.");
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

    public void Update()
    {
        using var message = _bus.PopFiltered(MessageType.Error | MessageType.Eos);
        if (message?.Type == MessageType.Error)
            throw new InvalidOperationException("Audio decoding or output failed.");
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
        _pipeline.SetState(State.Null);
        _bus.Dispose();
        _pipeline.Dispose();
    }
}
