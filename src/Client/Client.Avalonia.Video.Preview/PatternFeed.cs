using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Client.Core.Media;

namespace Client.Avalonia.Video.Preview;

/// <summary>
/// Feeds a moving test picture to a surface from its own thread, as a stream would. The bar moves
/// with the time rather than with the frames, so a lower framerate shows as a stutter and a stop
/// as a frozen picture.
/// </summary>
internal sealed unsafe class PatternFeed : IDisposable
{
    private const int Width = 1280;
    private const int Height = 720;
    private const int Stride = Width * 4;
    private const int BarWidth = 64;
    private const double BarSpeed = 400; // pixels per second

    private readonly Action<RawVideoFrame> _push;
    private readonly byte[] _background = CreateBackground();
    private readonly byte* _frame = (byte*)NativeMemory.Alloc(Stride * Height);
    private readonly Thread _thread;
    private volatile int _framerate;
    private volatile bool _isStopped;

    public PatternFeed(Action<RawVideoFrame> push, int framerate)
    {
        _push = push;
        _framerate = framerate;
        _thread = new Thread(Run) { IsBackground = true, Name = "pattern_feed" };
        _thread.Start();
    }

    /// <summary>Frames per second; 0 stops sending until it is raised again.</summary>
    public int Framerate
    {
        get => _framerate;
        set => _framerate = value;
    }

    private void Run()
    {
        var clock = Stopwatch.StartNew();

        while (!_isStopped)
        {
            var framerate = _framerate;

            if (framerate <= 0)
            {
                Thread.Sleep(50);
                continue;
            }

            var time = clock.Elapsed;
            Draw(time.TotalSeconds);
            _push(new RawVideoFrame((IntPtr)_frame, Width, Height, Stride) { Timestamp = (long)(time.TotalMilliseconds * 1_000_000) });

            var next = time + TimeSpan.FromSeconds(1.0 / framerate);
            var wait = next - clock.Elapsed;

            if (wait > TimeSpan.Zero)
                Thread.Sleep(wait);
        }
    }

    private void Draw(double seconds)
    {
        var span = new Span<byte>(_frame, Stride * Height);
        _background.CopyTo(span);

        // Back and forth across the picture
        var travel = Width - BarWidth;
        var position = (int)(seconds * BarSpeed % (travel * 2));
        var left = position < travel ? position : travel * 2 - position;

        for (var y = 0; y < Height; y++)
            span.Slice(y * Stride + left * 4, BarWidth * 4).Fill(255);
    }

    private static byte[] CreateBackground()
    {
        var pixels = new byte[Stride * Height];

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var offset = y * Stride + x * 4;
                var isGrid = x % 80 == 0 || y % 80 == 0;

                pixels[offset] = isGrid ? (byte)200 : (byte)(x * 255 / Width);
                pixels[offset + 1] = isGrid ? (byte)200 : (byte)(y * 255 / Height);
                pixels[offset + 2] = isGrid ? (byte)200 : (byte)64;
                pixels[offset + 3] = 255;
            }
        }

        return pixels;
    }

    public void Dispose()
    {
        _isStopped = true;
        _thread.Join();
        NativeMemory.Free(_frame);
    }
}
