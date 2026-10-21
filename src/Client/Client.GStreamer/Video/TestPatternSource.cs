using Gst;
using Client.GStreamer.Interop;

namespace Client.GStreamer.Video;

/// <summary>
/// A live source of a moving test picture, drawn here and pushed through appsrc: the bundled
/// runtime carries no videotestsrc. A bar travels across a gradient, so a frozen or a torn stream
/// is easy to tell from a working one.
/// </summary>
internal sealed class TestPatternSource : IDisposable
{
    private const int Width = 640;
    private const int Height = 360;
    private const int BarWidth = 40;
    private const string ElementName = "pattern";

    private readonly Element _source;
    private readonly int _framerate;
    private readonly Thread _thread;
    private volatile bool _isStopped;

    private TestPatternSource(Element source, int framerate)
    {
        _source = source;
        _framerate = framerate;
        _thread = new Thread(Run) { IsBackground = true, Name = "test_pattern" };
    }

    public static VideoSource? Create(int framerate)
    {
        var bin = Global.ParseBinFromDescription(
            $"appsrc name={ElementName} is-live=true do-timestamp=true format=time " +
            $"caps=\"video/x-raw,format=BGRx,width={Width},height={Height},framerate={framerate}/1,pixel-aspect-ratio=1/1\"",
            true);

        if (bin?.GetByName(ElementName) is not { } source)
        {
            bin?.Dispose();
            return null;
        }

        var pattern = new TestPatternSource(source, framerate);
        pattern._thread.Start();

        return new VideoSource(bin, pattern);
    }

    private void Run()
    {
        var interval = TimeSpan.FromSeconds(1.0 / _framerate);
        var background = CreateBackground();
        var frame = new byte[background.Length];

        for (var index = 0; !_isStopped; index++)
        {
            background.CopyTo(frame, 0);
            DrawBar(frame, index * 8 % (Width - BarWidth));

            // Refused until the pipeline plays and after it stops; the next frame is tried again
            using (var buffer = Gst.Buffer.NewMemdup(frame))
                GstApp.PushBuffer(_source, buffer);

            Thread.Sleep(interval);
        }
    }

    private static byte[] CreateBackground()
    {
        var pixels = new byte[Width * Height * 4];

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var offset = (y * Width + x) * 4;

                pixels[offset] = (byte)(x * 255 / Width);
                pixels[offset + 1] = (byte)(y * 255 / Height);
                pixels[offset + 2] = 64;
            }
        }

        return pixels;
    }

    private static void DrawBar(byte[] frame, int left)
    {
        for (var y = 0; y < Height; y++)
            frame.AsSpan((y * Width + left) * 4, BarWidth * 4).Fill(255);
    }

    public void Dispose()
    {
        _isStopped = true;

        if (Thread.CurrentThread == _thread)
            return;

        _thread.Join();
        _source.Dispose();
    }
}
