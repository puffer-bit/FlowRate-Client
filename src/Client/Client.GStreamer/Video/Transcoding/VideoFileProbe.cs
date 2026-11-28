using Gst;
using Client.Core.Chat;

namespace Client.GStreamer.Video.Transcoding;

/// <summary>
/// A video file as its parsers see it, without decoding it.
/// </summary>
/// <param name="Orientation">The rotation it is shown with, e.g. "rotate-90"; null for none.</param>
/// <param name="Tags">The names of every tag the file carries, its own and of its streams.</param>
public sealed record VideoFileInfo(VideoFacts Facts, string? Orientation, IReadOnlySet<string> Tags);

public static class VideoFileProbe
{
    private static readonly ClockTime PollTimeout = ClockTime.FromNanoseconds(100_000_000);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <returns>Null when the file is not something the runtime can parse into streams.</returns>
    public static VideoFileInfo? Probe(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        Mp4Layout layout;
        try
        {
            layout = Mp4Layout.Read(fullPath);
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException)
        {
            layout = new Mp4Layout(false, false);
        }

        using var pipeline = (Pipeline)Global.ParseLaunch("filesrc name=file ! parsebin name=parse");
        using (var file = pipeline.GetByName("file")!)
            file.SetProperty("location", fullPath);

        var streams = new List<Pad>();
        var tags = new HashSet<string>();
        string? orientation = null;
        var sync = new object();
        // The probes stay referenced while the pipeline runs
        var probes = new List<PadProbeCallback>();
        Exception? linkError = null;

        using var parse = pipeline.GetByName("parse")!;
        parse.PadAdded += (_, args) =>
        {
            try
            {
                var pad = args.NewPad;
                PadProbeCallback probe = (_, info) =>
                {
                    using var @event = info.GetEvent();
                    if (@event?.Type != EventType.Tag)
                        return PadProbeReturn.Ok;
                    @event.ParseTag(out var list);
                    if (list == null)
                        return PadProbeReturn.Ok;
                    lock (sync)
                    {
                        for (uint index = 0; index < list.NTags(); index++)
                            tags.Add(list.NthTagName(index));
                        if (list.GetString("image-orientation", out var value))
                            orientation ??= value;
                    }
                    return PadProbeReturn.Ok;
                };
                // Each stream prerolls its own sink: one demuxer thread feeds them all, and without the
                // queue the first sink to preroll would hold back the others
                var sink = Global.ParseBinFromDescription(
                    "queue max-size-buffers=0 max-size-time=0 max-size-bytes=67108864 ! fakesink sync=false", true)
                    ?? throw new InvalidOperationException("The runtime has no fakesink.");
                pipeline.Add(sink);
                sink.SyncStateWithParent();
                using var sinkPad = sink.GetStaticPad("sink")!;
                lock (sync)
                {
                    probes.Add(probe);
                    streams.Add(pad);
                }
                pad.AddProbe(PadProbeType.EventDownstream, probe);
                if (pad.Link(sinkPad) != PadLinkReturn.Ok)
                    throw new InvalidOperationException("A parsed stream could not be linked.");
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(ref linkError, exception, null);
            }
        };

        try
        {
            if (pipeline.SetState(State.Paused) == StateChangeReturn.Failure)
                return null;

            using var bus = pipeline.GetBus();
            var deadline = System.DateTimeOffset.UtcNow + Timeout;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Volatile.Read(ref linkError) is { } error)
                    throw new InvalidOperationException("The file could not be probed.", error);
                if (System.DateTimeOffset.UtcNow > deadline)
                    return null;
                using var message = bus.TimedPopFiltered(PollTimeout, MessageType.AsyncDone | MessageType.Error);
                if (message == null)
                    continue;
                if (message.Type == MessageType.Error)
                    return null;
                break;
            }

            var duration = pipeline.QueryDuration(Format.Time, out var nanoseconds) && nanoseconds > 0
                ? TimeSpan.FromTicks(nanoseconds / 100)
                : TimeSpan.Zero;

            string? videoCodec = null, audioCodec = null;
            int width = 0, height = 0;
            double frameRate = 0;
            lock (sync)
            {
                foreach (var pad in streams)
                {
                    using var caps = pad.GetCurrentCaps();
                    if (caps == null || caps.GetSize() == 0)
                        continue;
                    using var structure = caps.GetStructure(0);
                    if (structure == null)
                        continue;
                    var name = structure.GetName();
                    if (name.StartsWith("video/", StringComparison.Ordinal) && videoCodec == null)
                    {
                        videoCodec = VideoCodecName(name);
                        structure.GetInt("width", out width);
                        structure.GetInt("height", out height);
                        // Non-square pixels are counted as they are shown, which is how the picture is scaled
                        if (structure.GetFraction("pixel-aspect-ratio", out var parN, out var parD) && parN > 0 && parD > 0 && parN != parD)
                            width = (int)Math.Round((double)width * parN / parD);
                        if (structure.GetFraction("framerate", out var rateN, out var rateD) && rateN > 0 && rateD > 0)
                            frameRate = (double)rateN / rateD;
                    }
                    else if (name.StartsWith("audio/", StringComparison.Ordinal) && audioCodec == null)
                    {
                        audioCodec = AudioCodecName(name,
                            structure.GetInt("mpegversion", out var version) ? version : 0);
                    }
                }
            }

            if (videoCodec == null)
                return null;

            var facts = new VideoFacts(videoCodec, width, height, frameRate, audioCodec, duration, layout.IsMp4,
                layout.IsFastStart, new FileInfo(fullPath).Length);
            lock (sync)
                return new VideoFileInfo(facts, orientation, tags.ToHashSet());
        }
        finally
        {
            pipeline.SetState(State.Null);
            lock (sync)
            {
                foreach (var pad in streams)
                    pad.Dispose();
            }
            GC.KeepAlive(probes);
        }
    }

    private static string VideoCodecName(string capsName) => capsName switch
    {
        "video/x-h264" => "h264",
        "video/x-h265" => "h265",
        "video/x-av1" => "av1",
        "video/x-vp9" => "vp9",
        "video/x-vp8" => "vp8",
        _ => capsName.StartsWith("video/x-", StringComparison.Ordinal) ? capsName["video/x-".Length..] : capsName
    };

    private static string AudioCodecName(string capsName, int mpegVersion) => capsName switch
    {
        "audio/mpeg" => mpegVersion == 1 ? "mp3" : "aac",
        "audio/x-opus" => "opus",
        "audio/x-raw" => "pcm",
        _ => capsName.StartsWith("audio/x-", StringComparison.Ordinal) ? capsName["audio/x-".Length..] : capsName
    };
}
