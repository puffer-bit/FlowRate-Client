using System.Diagnostics;
using Client.GStreamer.Video.Transcoding;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Takes the frame a sent video is shown with from generated files: its size, its rotation, a short video,
/// and a file without a picture.
/// </summary>
internal static class VideoFrameSmokeChecks
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "flowrate-video-frame-smoke");

    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        var grabber = new GstVideoFrameGrabber(NullLogger<GstVideoFrameGrabber>.Instance);
        if (!grabber.IsAvailable)
            throw new InvalidOperationException("The media runtime cannot take frames.");

        var plain = Generate("plain.mp4", 1280, 720, 4, rotate: false);
        Check(grabber, plain, "1280x720, 4 s", 640, 360, 0);

        var turned = Generate("turned.mp4", 1280, 720, 4, rotate: true);
        Check(grabber, turned, "turned by its tag", 640, 360, 90);

        var small = Generate("small.mp4", 320, 240, 4, rotate: false);
        Check(grabber, small, "smaller than the frame", 320, 240, 0);

        var moment = Generate("moment.mp4", 640, 360, 1, rotate: false);
        Check(grabber, moment, "1 s long", 640, 360, 0);

        var tone = Path.Combine(Folder, "tone.wav");
        File.WriteAllBytes(tone, new byte[44]);
        var none = grabber.GrabAsync(tone, 640).GetAwaiter().GetResult();
        if (none != null)
            throw new InvalidOperationException("A file without a picture gave a frame.");
        Console.WriteLine("  no picture: none, as it should");

        Console.WriteLine("Video frame smoke checks passed.");
    }

    private static string Generate(string name, int width, int height, int seconds, bool rotate)
    {
        var path = Path.Combine(Folder, name);
        VideoTranscodeSmokeChecks.Generate(path, width, height, 30, seconds,
            "openh264enc bitrate=1500000 ! h264parse", "mp4mux name=mux faststart=true", "fdkaacenc",
            rotate, tagged: false);
        return path;
    }

    private static void Check(GstVideoFrameGrabber grabber, string path, string what, int width, int height,
        int rotation)
    {
        var watch = Stopwatch.StartNew();
        var frame = grabber.GrabAsync(path, 640).GetAwaiter().GetResult()
                    ?? throw new InvalidOperationException($"{what}: no frame.");
        if ((frame.Width, frame.Height, frame.Rotation) != (width, height, rotation))
            throw new InvalidOperationException(
                $"{what}: {frame.Width}x{frame.Height} turned {frame.Rotation}, expected {width}x{height} turned {rotation}.");
        if (frame.Bgra.Length != width * height * 4)
            throw new InvalidOperationException($"{what}: {frame.Bgra.Length} bytes.");
        // A picture, not a blank: its pixels differ
        var first = BitConverter.ToInt32(frame.Bgra, 0);
        var varied = false;
        for (var offset = 0; offset < frame.Bgra.Length && !varied; offset += 4 * 97)
            varied = BitConverter.ToInt32(frame.Bgra, offset) != first;
        if (!varied)
            throw new InvalidOperationException($"{what}: the frame is one colour.");
        Console.WriteLine($"  {what}: {frame.Width}x{frame.Height} turned {frame.Rotation} in {watch.ElapsedMilliseconds} ms");
    }
}
