using System.Globalization;
using Gst;
using Task = System.Threading.Tasks.Task;
using Client.Core.Chat;
using Client.GStreamer.Interop;

namespace Client.GStreamer.Video.Transcoding;

public sealed class VideoTranscodeException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// How a video was compressed.
/// </summary>
/// <param name="VideoEncoder">The encoder element; null when the streams were copied as they are.</param>
public sealed record VideoTranscodeResult(string? VideoEncoder, string? AudioEncoder, int Width, int Height, int FrameRate);

/// <summary>
/// Writes a video file again as a faststart MP4: encoded to H.264 and AAC, or with its streams copied.
/// Either way it keeps no tag of the original but the rotation, so neither where nor when it was taken
/// nor by what goes with it.
/// </summary>
public static class VideoTranscoder
{
    private static readonly ClockTime PollTimeout = ClockTime.FromNanoseconds(100_000_000);

    // The one tag that is kept: without it a video shot upright plays lying on its side
    private const string OrientationTag = "image-orientation";

    /// <summary>
    /// Encodes <paramref name="input"/> to <paramref name="preset"/> into <paramref name="output"/>,
    /// with the hardware encoder where the system has one and openh264 where it fails.
    /// </summary>
    /// <param name="progress">From 0 to 1, by the time of the video encoded.</param>
    /// <exception cref="VideoTranscodeException">The file could not be decoded or encoded.</exception>
    public static async Task<VideoTranscodeResult> TranscodeAsync(string input, string output, VideoFileInfo info,
        VideoCompressionPreset preset, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var facts = info.Facts;
        var (width, height) = preset.Fit(facts.Width, facts.Height);
        var frameRate = preset.FrameRateFor(facts.FrameRate);
        // A keyframe each two seconds, for seeking
        var encoders = VideoFileEncoders.H264(preset.VideoKbpsAt(frameRate), frameRate * 2);
        if (encoders.Count == 0)
            throw new VideoTranscodeException("The runtime has no H.264 encoder.");
        var audio = facts.AudioCodec != null ? VideoFileEncoders.Audio(VideoCompressionPreset.AudioKbps) : null;

        var video = string.Format(CultureInfo.InvariantCulture,
            "videoconvertscale ! videorate ! video/x-raw,width={0},height={1},framerate={2}/1,pixel-aspect-ratio=1/1",
            width, height, frameRate);
        // Up to stereo: the AAC encoders take more, but a phone speaker or headphones play two
        var audioBranch = audio == null ? null
            : $"audioconvert ! audioresample ! audio/x-raw,rate=48000,channels=[1,2] ! {audio.LaunchString} ! queue";

        VideoTranscodeException? failure = null;
        foreach (var encoder in encoders)
        {
            try
            {
                await RunAsync(input, output, "uridecodebin name=demux",
                    $"{video} ! {encoder.LaunchString} ! h264parse ! queue", audioBranch,
                    facts.Duration, progress, cancellationToken);
                return new VideoTranscodeResult(encoder.Factory, audio?.Factory, width, height, frameRate);
            }
            catch (VideoTranscodeException exception) when (encoder.IsHardware)
            {
                // A hardware encoder may refuse a size or run out of sessions; the software one is next
                failure = exception;
            }
        }
        throw failure ?? new VideoTranscodeException("No encoder could encode the video.");
    }

    /// <summary>
    /// Copies the streams of <paramref name="input"/> into <paramref name="output"/> as they are.
    /// </summary>
    /// <exception cref="VideoTranscodeException">The streams could not be put into an MP4.</exception>
    public static async Task<VideoTranscodeResult> RemuxAsync(string input, string output, VideoFileInfo info,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        await RunAsync(input, output, "filesrc name=source ! parsebin name=demux", "queue",
            info.Facts.AudioCodec != null ? "queue" : null, info.Facts.Duration, progress, cancellationToken);
        return new VideoTranscodeResult(null, null, info.Facts.Width, info.Facts.Height,
            (int)Math.Round(info.Facts.FrameRate));
    }

    private static async Task RunAsync(string input, string output, string source, string videoBranch,
        string? audioBranch, TimeSpan duration, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var inputPath = Path.GetFullPath(input);
        var outputPath = Path.GetFullPath(output);
        // On the disk of the output: the index is put in front by copying the media after it
        var moovPath = outputPath + ".moov";

        try
        {
            await Task.Run(() =>
            {
                using var run = new Run(source, videoBranch, audioBranch, inputPath, outputPath, moovPath, duration, progress);
                run.Wait(cancellationToken);
            }, CancellationToken.None);
        }
        catch
        {
            TryDelete(outputPath);
            throw;
        }
        finally
        {
            TryDelete(moovPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// One pipeline from the demuxer or decoder to the file: the streams it exposes are linked as they come.
    /// </summary>
    private sealed class Run : IDisposable
    {
        private readonly Pipeline _pipeline;
        private readonly Element _demux;
        private readonly Element _mux;
        private readonly string _videoBranch;
        private readonly string? _audioBranch;
        private readonly TimeSpan _duration;
        private readonly IProgress<double>? _progress;
        private readonly object _sync = new();
        // Native code calls them while the pipeline runs
        private readonly List<PadProbeCallback> _probes = [];
        private readonly List<IDisposable> _owned = [];
        private bool _hasVideo;
        private bool _hasAudio;
        private Exception? _linkError;
        private int _reportedPercent = -1;

        public Run(string source, string videoBranch, string? audioBranch, string input, string output,
            string moovFile, TimeSpan duration, IProgress<double>? progress)
        {
            _videoBranch = videoBranch;
            _audioBranch = audioBranch;
            _duration = duration;
            _progress = progress;
            _pipeline = (Pipeline)Global.ParseLaunch(
                $"{source} mp4mux name=mux faststart=true ! filesink name=output sync=false async=false");
            _demux = _pipeline.GetByName("demux")!;
            _mux = _pipeline.GetByName("mux")!;
            if (source.StartsWith("uridecodebin", StringComparison.Ordinal))
                _demux.SetProperty("uri", new System.Uri(input).AbsoluteUri);
            else
                using (var file = _pipeline.GetByName("source")!)
                    file.SetProperty("location", input);
            _mux.SetProperty("faststart-file", moovFile);
            using (var sink = _pipeline.GetByName("output")!)
                sink.SetProperty("location", output);
            _demux.PadAdded += OnPadAdded;
        }

        public void Wait(CancellationToken cancellationToken)
        {
            using var bus = _pipeline.GetBus();
            if (_pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
                throw Failure(bus, "The pipeline could not start.");

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Volatile.Read(ref _linkError) is { } linkError)
                    throw new VideoTranscodeException(linkError.Message, linkError);
                using var message = bus.TimedPopFiltered(PollTimeout, MessageType.Eos | MessageType.Error);
                if (message == null)
                    continue;
                if (message.Type == MessageType.Error)
                    throw Describe(message);
                if (!_hasVideo)
                    throw new VideoTranscodeException("The file has no video stream that could be read.");
                _progress?.Report(1);
                return;
            }
        }

        private static VideoTranscodeException Failure(Bus bus, string fallback)
        {
            using var message = bus.TimedPopFiltered(ClockTime.FromNanoseconds(0), MessageType.Error);
            return message != null ? Describe(message) : new VideoTranscodeException(fallback);
        }

        private static VideoTranscodeException Describe(Message message)
        {
            var (error, debug) = message.ParseError();
            return new VideoTranscodeException($"{error.Message} ({debug})");
        }

        private void OnPadAdded(object? sender, Element.PadAddedSignalArgs args)
        {
            var pad = args.NewPad;
            try
            {
                using var caps = pad.GetCurrentCaps() ?? pad.QueryCaps(null);
                string name;
                using (var structure = caps.GetSize() > 0 ? caps.GetStructure(0) : null)
                    name = structure?.GetName() ?? "";
                string? branch = null, template = null;
                var reportsProgress = false;
                lock (_sync)
                {
                    if (name.StartsWith("video/", StringComparison.Ordinal) && !_hasVideo)
                    {
                        _hasVideo = true;
                        (branch, template, reportsProgress) = (_videoBranch, "video_%u", true);
                    }
                    else if (name.StartsWith("audio/", StringComparison.Ordinal) && !_hasAudio && _audioBranch != null)
                    {
                        _hasAudio = true;
                        (branch, template) = (_audioBranch, "audio_%u");
                    }
                }

                // A second video or audio stream, subtitles, data: left out
                if (branch == null)
                {
                    LinkTo(pad, "fakesink sync=false async=false", null, false);
                    return;
                }
                LinkTo(pad, branch, template, reportsProgress);
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(ref _linkError, exception, null);
            }
            finally
            {
                pad.Dispose();
            }
        }

        private void LinkTo(Pad pad, string description, string? muxTemplate, bool reportsProgress)
        {
            var bin = Global.ParseBinFromDescription(description, true)
                ?? throw new InvalidOperationException($"Cannot create \"{description}\".");
            _pipeline.Add(bin);
            using var sink = bin.GetStaticPad("sink")
                ?? throw new InvalidOperationException($"\"{description}\" takes no input.");

            if (muxTemplate != null)
            {
                using var source = bin.GetStaticPad("src")
                    ?? throw new InvalidOperationException($"\"{description}\" has no output.");
                var muxPad = _mux.RequestPadSimple(muxTemplate)
                    ?? throw new InvalidOperationException($"The MP4 muxer takes no more {muxTemplate} streams.");
                lock (_sync)
                    _owned.Add(muxPad);
                AddProbe(source, PadProbeType.EventDownstream, FilterTags);
                // Measured where the frames come in: an encoder that reorders frames shifts the times it
                // puts out by a thousand hours
                if (reportsProgress)
                    AddProbe(sink, PadProbeType.Buffer, ReportProgress);
                if (source.Link(muxPad) != PadLinkReturn.Ok)
                    throw new InvalidOperationException($"The MP4 muxer does not take this {muxTemplate[..5]} stream.");
            }

            bin.SyncStateWithParent();
            var linked = pad.Link(sink);
            if (linked != PadLinkReturn.Ok)
            {
                using var caps = pad.GetCurrentCaps();
                throw new InvalidOperationException($"\"{description}\" does not take {caps}: {linked}.");
            }
            lock (_sync)
                _owned.Add(bin);
        }

        private void AddProbe(Pad pad, PadProbeType type, PadProbeCallback callback)
        {
            lock (_sync)
                _probes.Add(callback);
            pad.AddProbe(type, callback);
        }

        // Only the rotation reaches the muxer, which writes it as the matrix of the track: every
        // other tag, where and when it was taken, the camera, the title, is dropped
        private static PadProbeReturn FilterTags(Pad pad, PadProbeInfo info)
        {
            using var @event = info.GetEvent();
            if (@event?.Type != EventType.Tag)
                return PadProbeReturn.Ok;

            @event.ParseTag(out var list);
            if (list == null)
                return PadProbeReturn.Drop;
            var hasOrientation = list.GetString(OrientationTag, out var orientation) && orientation != null;
            if (hasOrientation && list.NTags() == 1)
                return PadProbeReturn.Ok;
            if (hasOrientation && list.GetScope() == TagScope.Stream)
            {
                var kept = TagList.NewEmpty();
                kept.AddString(TagMergeMode.Replace, OrientationTag, orientation!);
                // Comes back to this probe with the rotation alone, and passes
                pad.PushEvent(Event.NewTag(kept));
            }
            return PadProbeReturn.Drop;
        }

        private PadProbeReturn ReportProgress(Pad pad, PadProbeInfo info)
        {
            if (_progress == null || _duration <= TimeSpan.Zero)
                return PadProbeReturn.Ok;
            using var buffer = info.GetBuffer();
            if (buffer == null)
                return PadProbeReturn.Ok;
            var pts = GstVideoNative.GetPts(buffer.Handle);
            if (pts < 0)
                return PadProbeReturn.Ok;
            var fraction = Math.Clamp(pts / 100d / _duration.Ticks, 0, 1);
            var percent = (int)(fraction * 100);
            if (percent > Volatile.Read(ref _reportedPercent))
            {
                Volatile.Write(ref _reportedPercent, percent);
                _progress.Report(fraction);
            }
            return PadProbeReturn.Ok;
        }

        public void Dispose()
        {
            _pipeline.SetState(State.Null);
            _demux.PadAdded -= OnPadAdded;
            lock (_sync)
            {
                foreach (var owned in _owned)
                    owned.Dispose();
                _owned.Clear();
            }
            _demux.Dispose();
            _mux.Dispose();
            _pipeline.Dispose();
            GC.KeepAlive(_probes);
        }
    }
}
