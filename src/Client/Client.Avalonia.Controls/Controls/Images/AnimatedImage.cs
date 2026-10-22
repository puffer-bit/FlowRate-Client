using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Client.Avalonia.Controls.Images;

/// <summary>
/// The frames of an animated picture, shown one after another by any control that shows an image.
/// A control redraws a source that tells it it changed, so nothing in the views has to know the
/// picture moves.
/// </summary>
/// <remarks>
/// A drawing image only for that telling, which Avalonia keeps to its own sources; the drawing
/// itself is not used, the frames are drawn instead.
/// </remarks>
public sealed class AnimatedImage : DrawingImage, IImage, IDisposable
{
    // Browsers show a frame that asks for less than this for 100 ms, and GIFs are made with that in mind
    private static readonly TimeSpan MinDelay = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan ShortDelay = TimeSpan.FromMilliseconds(100);

    private readonly Bitmap[] _frames;
    private readonly TimeSpan[] _delays;
    private DispatcherTimer? _timer;
    private int _current;
    private bool _drawn;
    private bool _disposed;

    /// <param name="frames">Each frame whole, of one size, and how long it is shown; owned by the image.</param>
    public AnimatedImage(IReadOnlyList<(Bitmap Frame, TimeSpan Delay)> frames)
    {
        if (frames.Count == 0)
            throw new ArgumentException("An animated image needs at least one frame.", nameof(frames));

        _frames = new Bitmap[frames.Count];
        _delays = new TimeSpan[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            _frames[i] = frames[i].Frame;
            _delays[i] = frames[i].Delay < MinDelay ? ShortDelay : frames[i].Delay;
        }
    }

    public new Size Size => _frames[0].Size;

    /// <summary>The picture held still on its first frame; owned by the image.</summary>
    public Bitmap FirstFrame => _frames[0];

    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
    {
        if (_disposed)
            return;

        context.DrawImage(_frames[_current], sourceRect, destRect);
        _drawn = true;

        if (_frames.Length > 1 && _timer is not { IsEnabled: true })
        {
            _timer ??= new DispatcherTimer(DispatcherPriority.Render);
            _timer.Tick -= OnTick;
            _timer.Tick += OnTick;
            _timer.Interval = _delays[_current];
            _timer.Start();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer?.Stop();
        foreach (var frame in _frames)
            frame.Dispose();
    }

    // The frames go on only while something draws them: a running timer would keep the picture in
    // memory after every view of it is gone
    private void OnTick(object? sender, EventArgs e)
    {
        // Nothing drew the frame shown last: no view shows the picture now. The next one to draw it starts it again
        if (!_drawn || _disposed)
        {
            _timer?.Stop();
            return;
        }

        _drawn = false;
        _current = (_current + 1) % _frames.Length;
        _timer!.Interval = _delays[_current];
        RaiseInvalidated(EventArgs.Empty);
    }
}
