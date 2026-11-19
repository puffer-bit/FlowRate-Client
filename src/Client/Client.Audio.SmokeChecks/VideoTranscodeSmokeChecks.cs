using System.Diagnostics;
using Client.Application.Chat;
using Client.Application.Ports;
using Client.Core.Chat;
using Microsoft.Extensions.Logging.Abstractions;
using Client.GStreamer.Video.Transcoding;
using Gst;
using Gst.App;
using Task = System.Threading.Tasks.Task;

/// <summary>
/// Compresses and remuxes videos made here: a 1080p60 H.264 MP4 with sound, a rotation and a location,
/// and a silent VP8 WebM. With paths after --video-transcode, those files are compressed as well;
/// with --software-rendering, by openh264 alone.
/// </summary>
internal static class VideoTranscodeSmokeChecks
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "flowrate-video-transcode-smoke");

    public static async Task Run(string[] args)
    {
        Directory.CreateDirectory(Folder);
        // With --software-rendering the hardware encoders are left out, as in the client
        Client.Tools.AppInfo.LaunchOptions.Configure(args.Where(arg => arg == "--software-rendering").ToList());
        CheckDecisions();

        var phone = Path.Combine(Folder, "phone.mp4");
        Generate(phone, 1920, 1080, 60, 4, "openh264enc bitrate=40000000 rate-control=quality ! h264parse",
            "mp4mux name=mux", "fdkaacenc", rotate: true, tagged: true, interleaveAtEnd: true);
        var source = Probe(phone);
        Require(source.Facts is { VideoCodec: "h264", Width: 1920, Height: 1080, AudioCodec: "aac", IsMp4: true },
            $"Generated phone video is probed: {source.Facts}");
        Require(Math.Abs(source.Facts.FrameRate - 60) < 0.5, $"Generated video is 60 fps: {source.Facts.FrameRate}");
        Require(source.Orientation == "rotate-90", $"Generated video is rotated: {source.Orientation}");
        Require(source.Tags.Contains("geo-location-latitude") || source.Tags.Contains("title"),
            $"Generated video carries metadata: {string.Join(", ", source.Tags)}");
        Require(VideoPreparationRules.Decide(source.Facts, true, true, VideoCompressionPreset.Medium) == VideoPreparationKind.Transcode,
            "A 1080p video above the bitrate is compressed to 720p");

        // Compressed
        var compressed = Path.Combine(Folder, "phone-720p.mp4");
        var reports = new List<double>();
        var clock = Stopwatch.StartNew();
        var result = await VideoTranscoder.TranscodeAsync(phone, compressed, source, VideoCompressionPreset.Medium,
            new SyncProgress(reports.Add));
        Console.WriteLine($"  compressed in {clock.Elapsed.TotalSeconds:F1} s with {result.VideoEncoder} + {result.AudioEncoder}: " +
                          $"{new FileInfo(phone).Length / 1024} KiB -> {new FileInfo(compressed).Length / 1024} KiB");
        var output = Probe(compressed);
        Require(output.Facts is { VideoCodec: "h264", Width: 1280, Height: 720, IsMp4: true, IsFastStart: true },
            $"Compressed video is a 720p faststart H.264 MP4: {output.Facts}");
        Require(output.Facts.AudioCodec is "aac" or "opus", $"Compressed video keeps its sound: {output.Facts.AudioCodec}");
        Require(Math.Abs(output.Facts.FrameRate - 60) < 0.5, $"Compressed video keeps 60 fps: {output.Facts.FrameRate}");
        Require(Math.Abs((output.Facts.Duration - source.Facts.Duration).TotalSeconds) < 0.2,
            $"Compressed video keeps its length: {output.Facts.Duration} of {source.Facts.Duration}");
        Require(output.Facts.Size < source.Facts.Size / 3, "Compressed video is much smaller");
        Require(output.Orientation == "rotate-90", $"Compressed video keeps its rotation: {output.Orientation}");
        RequireNoMetadata(output, "Compressed");
        Require(reports.Count > 3 && reports[^1] >= 1 && reports.SequenceEqual(reports.Order()),
            $"Compression reports its progress: {reports.Count} reports up to {(reports.Count > 0 ? reports[^1] : 0):F2}");
        Require(VideoPreparationRules.Decide(output.Facts, true, true, VideoCompressionPreset.Medium) == VideoPreparationKind.Remux,
            "A compressed video is not compressed again");

        // Through the port the client uses, from a stream as the platform may give it
        var preparer = new GstVideoPreparer(NullLogger<GstVideoPreparer>.Instance);
        Require(preparer.IsAvailable, "Video preparer is available");
        var prepared = await preparer.PrepareAsync(
            new ChatMediaSource("phone.MOV", source.Facts.Size, () => Task.FromResult<System.IO.Stream>(File.OpenRead(phone))),
            new VideoPreparation(true, true, VideoCompressionPreset.Medium));
        Require(prepared is { Kind: VideoPreparationKind.Transcode, FileName: "phone.mp4" } && File.Exists(prepared.Path),
            $"Preparer compresses a streamed video into a new MP4: {prepared?.Kind} {prepared?.FileName}");
        prepared!.Dispose();
        Require(!File.Exists(prepared.Path), "A prepared video is deleted with it");
        var small = await preparer.PrepareAsync(new ChatMediaSource("small.mp4", output.Facts.Size,
                () => Task.FromResult<System.IO.Stream>(File.OpenRead(compressed)), compressed),
            new VideoPreparation(true, false, VideoCompressionPreset.Medium));
        Require(small is { Kind: VideoPreparationKind.AsIs, Path: null }, $"A compressed video goes as it is: {small?.Kind}");

        // Only the metadata removed
        Require(!source.Facts.IsFastStart, "Generated phone video has its index at the end");
        Require(VideoPreparationRules.Decide(source.Facts, false, true, VideoCompressionPreset.Medium) == VideoPreparationKind.Remux,
            "A video sent uncompressed without metadata is remuxed");
        var remuxed = Path.Combine(Folder, "phone-remuxed.mp4");
        await VideoTranscoder.RemuxAsync(phone, remuxed, source);
        var copy = Probe(remuxed);
        Require(copy.Facts with { IsFastStart = false, Size = 0 } == source.Facts with { Size = 0 },
            $"Remuxed video has the same streams: {copy.Facts}");
        Require(copy.Facts.IsFastStart, "Remuxed video has its index first");
        Require(Math.Abs(copy.Facts.Size - source.Facts.Size) < source.Facts.Size / 50, "Remuxed video keeps its size");
        Require(copy.Orientation == "rotate-90", $"Remuxed video keeps its rotation: {copy.Orientation}");
        RequireNoMetadata(copy, "Remuxed");

        // A silent WebM of another codec
        var webm = Path.Combine(Folder, "silent.webm");
        Generate(webm, 640, 480, 30, 2, "vp8enc deadline=1", "webmmux name=mux", null, rotate: false, tagged: false);
        var silent = Probe(webm);
        Require(silent.Facts is { VideoCodec: "vp8", AudioCodec: null, IsMp4: false }, $"Silent WebM is probed: {silent.Facts}");
        Require(VideoPreparationRules.Decide(silent.Facts, true, true, VideoCompressionPreset.Medium) == VideoPreparationKind.Transcode,
            "A VP8 video is encoded to H.264");
        Require(VideoPreparationRules.Decide(silent.Facts, false, true, VideoCompressionPreset.Medium) == VideoPreparationKind.AsIs,
            "A WebM sent uncompressed stays as it is");
        var silentOutput = Path.Combine(Folder, "silent.mp4");
        await VideoTranscoder.TranscodeAsync(webm, silentOutput, silent, VideoCompressionPreset.Medium);
        var silentFacts = Probe(silentOutput).Facts;
        Require(silentFacts is { VideoCodec: "h264", Width: 640, Height: 480, AudioCodec: null, IsFastStart: true },
            $"Silent video is not enlarged and stays silent: {silentFacts}");

        // Cancelled midway
        var cancelled = Path.Combine(Folder, "cancelled.mp4");
        using (var cancellation = new CancellationTokenSource())
        {
            var started = new SyncProgress(fraction =>
            {
                if (fraction > 0.1)
                    cancellation.Cancel();
            });
            var thrown = false;
            try
            {
                await VideoTranscoder.TranscodeAsync(phone, cancelled, source, VideoCompressionPreset.High, started, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                thrown = true;
            }
            Require(thrown, "Cancelled compression throws");
            Require(!File.Exists(cancelled) && !File.Exists(cancelled + ".moov"), "Cancelled compression leaves no files");
        }

        // Not a video
        var text = Path.Combine(Folder, "text.mp4");
        await File.WriteAllTextAsync(text, "not a video");
        Require(VideoFileProbe.Probe(text) == null, "A file that is not a video is not probed");

        foreach (var path in args.SkipWhile(arg => arg != "--video-transcode").Skip(1).Where(arg => !arg.StartsWith("--", StringComparison.Ordinal)))
            await CompressUserFile(path);

        Console.WriteLine("Video transcode smoke checks passed.");
    }

    private static void CheckDecisions()
    {
        var small = new VideoFacts("h264", 1280, 720, 30, "aac", TimeSpan.FromSeconds(10), true, true, 3_000_000);
        Require(VideoPreparationRules.Decide(small, true, false, VideoCompressionPreset.Medium) == VideoPreparationKind.AsIs,
            "A small faststart H.264 MP4 is sent as it is");
        Require(VideoPreparationRules.Decide(small, true, true, VideoCompressionPreset.Medium) == VideoPreparationKind.Remux,
            "A small video is only remuxed to remove metadata");
        Require(VideoPreparationRules.Decide(small with { IsFastStart = false }, false, false, VideoCompressionPreset.Medium) == VideoPreparationKind.Remux,
            "A video with its index at the end is remuxed");
        Require(VideoPreparationRules.Decide(small with { VideoCodec = "h265" }, true, false, VideoCompressionPreset.Medium) == VideoPreparationKind.Transcode,
            "An H.265 video is compressed to H.264");
        Require(VideoPreparationRules.Decide(small with { Width = 720, Height = 1280 }, true, false, VideoCompressionPreset.Medium) == VideoPreparationKind.AsIs,
            "An upright 720p video fits 720p");
        Require(VideoCompressionPreset.Medium.Fit(1080, 1920) == (720, 1280), "An upright 1080p video is reduced to 720p");
        Require(VideoCompressionPreset.Medium.Fit(2560, 1080) == (1280, 540), "A wide video is bounded by its longer side");
        Require(VideoCompressionPreset.Low.FrameRateFor(60) == 30 && VideoCompressionPreset.Medium.FrameRateFor(0) == 30,
            "Frame rate is bounded by the preset");
    }

    private static async Task CompressUserFile(string path)
    {
        Console.WriteLine($"Compressing {path}...");
        var info = VideoFileProbe.Probe(path) ?? throw new InvalidOperationException("Not a video this runtime parses");
        Console.WriteLine($"  {info.Facts}, {info.Facts.Kbps} kbit/s, orientation {info.Orientation ?? "none"}, " +
                          $"tags: {string.Join(", ", info.Tags)}");
        foreach (var preset in new[] { VideoCompressionPreset.Low, VideoCompressionPreset.Medium, VideoCompressionPreset.High })
        {
            var kind = VideoPreparationRules.Decide(info.Facts, true, true, preset);
            var output = Path.Combine(Folder, $"{Path.GetFileNameWithoutExtension(path)}-{preset.ShortSide}p.mp4");
            var clock = Stopwatch.StartNew();
            var result = kind == VideoPreparationKind.Transcode
                ? await VideoTranscoder.TranscodeAsync(path, output, info, preset)
                : kind == VideoPreparationKind.Remux
                    ? await VideoTranscoder.RemuxAsync(path, output, info)
                    : null;
            if (result == null)
            {
                Console.WriteLine($"  {preset.ShortSide}p: sent as it is");
                continue;
            }
            var facts = Probe(output).Facts;
            Console.WriteLine($"  {preset.ShortSide}p: {kind} with {result.VideoEncoder ?? "copy"} in {clock.Elapsed.TotalSeconds:F1} s " +
                              $"-> {facts.Width}x{facts.Height} {facts.FrameRate:F1} fps, {facts.Size / 1024} KiB, {facts.Kbps} kbit/s, {output}");
        }
    }

    private static VideoFileInfo Probe(string path) =>
        VideoFileProbe.Probe(path) ?? throw new InvalidOperationException($"Cannot probe {path}");

    private static void RequireNoMetadata(VideoFileInfo info, string what)
    {
        string[] leaked = ["geo-location-latitude", "geo-location-longitude", "title", "comment", "device-model"];
        var found = leaked.Where(info.Tags.Contains).ToList();
        Require(found.Count == 0, $"{what} video has no metadata: {string.Join(", ", found)}");
    }

    /// <summary>
    /// Writes a moving picture of <paramref name="seconds"/> through <paramref name="encoder"/>: the bundled
    /// runtime has no videotestsrc.
    /// </summary>
    /// <param name="interleaveAtEnd">The index at the end, as a phone writes it.</param>
    internal static void Generate(string path, int width, int height, int framerate, int seconds, string encoder,
        string mux, string? audioEncoder, bool rotate, bool tagged, bool interleaveAtEnd = false)
    {
        File.Delete(path);
        var audio = audioEncoder == null ? ""
            : $" audiotestsrc num-buffers={seconds * 48000 / 1024} samplesperbuffer=1024 ! audio/x-raw,rate=48000,channels=2 ! " +
              $"audioconvert ! {audioEncoder} ! queue ! mux.";
        using var pipeline = (Pipeline)Global.ParseLaunch(
            $"appsrc name=frames format=bytes block=true max-bytes={width * height * 4} ! " +
            // Gives the frames their times by the frame rate
            $"rawvideoparse format=i420 width={width} height={height} framerate={framerate}/1 ! " +
            $"videoconvert ! {encoder} ! queue ! mux.{audio} {mux} ! filesink location=\"{path}\"");
        using var frames = pipeline.GetByName("frames")!;
        using var muxer = pipeline.GetByName("mux")!;
        if (interleaveAtEnd)
            muxer.SetProperty("faststart", false);
        pipeline.SetState(State.Playing);

        if (rotate || tagged)
        {
            var tags = Gst.TagList.NewEmpty();
            if (rotate)
                tags.AddString(TagMergeMode.Replace, "image-orientation", "rotate-90");
            if (tagged)
            {
                tags.AddString(TagMergeMode.Replace, "title", "smoke check");
                tags.AddDouble(TagMergeMode.Replace, "geo-location-latitude", 55.7558);
                tags.AddDouble(TagMergeMode.Replace, "geo-location-longitude", 37.6173);
            }
            frames.SendEvent(Event.NewTag(tags));
        }

        var random = new Random(1);
        var size = width * height * 3 / 2;
        var frame = new byte[size];
        for (var index = 0; index < framerate * seconds; index++)
        {
            // A gradient that moves, with noise in a band, which costs bits to encode
            for (var y = 0; y < height; y++)
            {
                var row = frame.AsSpan(y * width, width);
                if (y % 64 < 2)
                    random.NextBytes(row);
                else
                    for (var x = 0; x < width; x++)
                        row[x] = (byte)(x + y + index * 4);
            }
            frame.AsSpan(width * height).Fill((byte)(128 + index % 64));
            using var buffer = Gst.Buffer.NewMemdup(frame);
            if (((AppSrc)frames).PushBuffer(buffer) != FlowReturn.Ok)
                throw new InvalidOperationException("Cannot push a generated frame");
        }
        ((AppSrc)frames).EndOfStream();

        using var bus = pipeline.GetBus();
        using var message = bus.TimedPopFiltered(ClockTime.FromNanoseconds(120_000_000_000), MessageType.Eos | MessageType.Error);
        pipeline.SetState(State.Null);
        if (message?.Type != MessageType.Eos)
            throw new InvalidOperationException($"Cannot generate {path}: {(message == null ? "timeout" : message.ParseError().Item1.Message)}");
    }

    private static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException($"Smoke check failed: {description}");
        Console.WriteLine($"  ok: {description}");
    }

    // Progress<T> posts to the thread pool; the checks want the reports in order
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value)
        {
            lock (this)
                report(value);
        }
    }
}
