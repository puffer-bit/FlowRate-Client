using System.Runtime.InteropServices;
using Client.Application.Ports;
using Client.Core.Media;
using Client.GStreamer.Loader;
using Gst;
using Gst.App;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace Client.GStreamer.Video.Transcoding;

/// <summary>
/// Takes the frame a sent video is shown with: playbin paused on it, the picture reduced and handed to an appsink.
/// </summary>
public sealed partial class GstVideoFrameGrabber(ILogger<GstVideoFrameGrabber> logger) : IVideoFrameGrabber
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private static readonly ClockTime PollTimeout = ClockTime.FromNanoseconds(100_000_000);
    private static readonly TimeSpan Into = TimeSpan.FromSeconds(1);

    private static readonly string[] Elements = ["playbin", "videoconvertscale", "appsink", "fakesink"];

    public bool IsAvailable => MediaRuntime.IsEnabled && GstLoader.Options != null &&
                               Elements.All(name => ElementFactory.Find(name) != null);

    public async Task<VideoFrameImage?> GrabAsync(string path, int maxSide, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return null;
        try
        {
            return await Task.Run(() => Grab(path, maxSide, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception);
            return null;
        }
    }

    private static VideoFrameImage? Grab(string path, int maxSide, CancellationToken cancellationToken)
    {
        // The sides and the rotation are known before anything is decoded
        var info = VideoFileProbe.Probe(path, cancellationToken);
        if (info is not { Facts: { Width: > 0, Height: > 0 } facts })
            return null;

        var scale = Math.Min(1.0, (double)maxSide / Math.Max(facts.Width, facts.Height));
        var width = Math.Max(2, (int)Math.Round(facts.Width * scale) & ~1);
        var height = Math.Max(2, (int)Math.Round(facts.Height * scale) & ~1);

        using var pipeline = (Pipeline)Global.ParseLaunch("playbin");
        using var output = Global.ParseBinFromDescription(
            $"videoconvertscale ! video/x-raw,format=BGRA,width={width},height={height},pixel-aspect-ratio=1/1 ! " +
            "appsink name=frame max-buffers=1 drop=true sync=false enable-last-sample=false", true);
        using var silence = ElementFactory.Make("fakesink", null);
        pipeline.SetProperty("video-sink", output);
        pipeline.SetProperty("audio-sink", silence);
        pipeline.SetProperty("uri", new System.Uri(Path.GetFullPath(path)).AbsoluteUri);
        using var sink = (AppSink)output.GetByName("frame")!;
        using var bus = pipeline.GetBus();
        try
        {
            if (pipeline.SetState(State.Paused) == StateChangeReturn.Failure || !WaitPrerolled(bus, cancellationToken))
                return null;

            // The very first frame is often black: one a second in, or a third of a shorter video
            var at = facts.Duration > TimeSpan.Zero && facts.Duration < Into * 3 ? facts.Duration / 3 : Into;
            if (facts.Duration > TimeSpan.Zero
                && pipeline.SeekSimple(Format.Time, SeekFlags.Flush | SeekFlags.Accurate, at.Ticks * 100)
                && !WaitPrerolled(bus, cancellationToken))
                return null;

            using var sample = sink.TryPullPreroll(ClockTime.FromNanoseconds(5_000_000_000));
            using var buffer = sample?.GetBuffer();
            if (buffer == null || !buffer.Map(out var map, MapFlags.Read))
                return null;
            try
            {
                var stride = width * 4;
                if ((long)map.Size < (long)stride * height)
                    return null;
                var pixels = new byte[stride * height];
                Marshal.Copy(map.DataPtr, pixels, 0, pixels.Length);
                return new VideoFrameImage(pixels, width, height, RotationOf(info.Orientation));
            }
            finally
            {
                buffer.Unmap(map);
            }
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    private static bool WaitPrerolled(Bus bus, CancellationToken cancellationToken)
    {
        var deadline = System.DateTimeOffset.UtcNow + Timeout;
        while (System.DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var message = bus.TimedPopFiltered(PollTimeout, MessageType.AsyncDone | MessageType.Error);
            if (message != null)
                return message.Type == MessageType.AsyncDone;
        }
        return false;
    }

    /// <summary>The clockwise turn an "image-orientation" tag asks for; mirrored ones are shown as stored.</summary>
    internal static int RotationOf(string? orientation) => orientation switch
    {
        "rotate-90" => 90,
        "rotate-180" => 180,
        "rotate-270" => 270,
        _ => 0
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Taking a frame of a video failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
