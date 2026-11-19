using Client.Application.Chat;
using Client.GStreamer.Video;
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

    // Set when the audio plays while it downloads
    private readonly StreamingSource? _streaming;

    /// <summary>True while the audio waits for bytes that have not arrived yet.</summary>
    public bool IsWaiting => _streaming?.IsWaiting == true;

    public ChatAudioPlayback(string path, double volume = 1) : this(path, null, "autoaudiosink", volume)
    {
    }

    /// <summary>Plays an attachment while it downloads, which the caller keeps until this is disposed.</summary>
    /// <param name="fileName">The name of the attachment, whose extension tells its container.</param>
    public ChatAudioPlayback(StreamingDownload download, string? fileName, double volume = 1)
        : this(null, new StreamingSource(download, fileName), "autoaudiosink", volume)
    {
    }

    internal ChatAudioPlayback(string path, string output, double volume = 1) : this(path, null, output, volume)
    {
    }

    internal ChatAudioPlayback(StreamingDownload download, string? fileName, string output, double volume = 1)
        : this(null, new StreamingSource(download, fileName), output, volume)
    {
    }

    private ChatAudioPlayback(string? path, StreamingSource? streaming, string output, double volume)
    {
        _streaming = streaming;
        _pipeline = (Pipeline)Global.ParseLaunch(
            (streaming != null ? "appsrc name=file" : "filesrc name=file") +
            " ! decodebin ! audioconvert ! audioresample ! volume name=level ! " + output);
        _bus = _pipeline.GetBus();
        try
        {
            // decodebin adds its type finder once it starts
            streaming?.ForceType(_pipeline);
            using var source = _pipeline.GetByName("file");
            if (streaming != null)
                streaming.Attach(source!);
            else
                source!.SetProperty("location", Path.GetFullPath(path!));
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
        // A read waiting for bytes elsewhere would hold the seek until they arrive
        _streaming?.InterruptReads();
        if (!_pipeline.SeekSimple(Format.Time, SeekFlags.Flush | SeekFlags.Accurate,
                (long)(seconds * 1_000_000_000)))
            return;
        HasEnded = false;
        Position = seconds;
    }

    public void Update()
    {
        using var message = _bus.PopFiltered(MessageType.Error | MessageType.Eos);
        if (_streaming?.Failure is { } failure)
            throw new IOException("The audio stopped downloading.", failure);
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
        _streaming?.Stop();
        _pipeline.SetState(State.Null);
        _bus.Dispose();
        _pipeline.Dispose();
    }
}
