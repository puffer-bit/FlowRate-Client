using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Client.Core.Media;

namespace Client.Views.Sessions.Hybrid.Video;

/// <summary>
/// Shows frames in memory by copying them into a bitmap that the renderer uploads. It works
/// everywhere, also without a GPU, and is what is left when nothing faster is.
/// </summary>
public class BitmapVideoPresenter : Control, IVideoPresenter
{
    private readonly VideoFrameQueue _queue = new();

    // Two bitmaps in turn: the one being filled is never the one being drawn
    private readonly WriteableBitmap?[] _bitmaps = new WriteableBitmap?[2];
    private int _shown = -1;

    // A refresh is asked for already; touched on the UI thread only
    private bool _isFrameRequested;
    private int _wakeQueued;

    public static readonly StyledProperty<StretchMode> StretchProperty =
        AvaloniaProperty.Register<BitmapVideoPresenter, StretchMode>(nameof(Stretch), StretchMode.Uniform);

    public StretchMode Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public FrameStats Stats => _queue.Stats;

    public bool PushFrame(RawVideoFrame frame)
    {
        // A GPU frame needs a presenter that can draw it
        if (frame.Gpu != null || !_queue.Enqueue(frame))
            return false;

        // One wake-up for any number of frames that arrive before it runs
        if (System.Threading.Interlocked.Exchange(ref _wakeQueued, 1) == 0)
            Dispatcher.UIThread.Post(Wake);

        return true;
    }

    public void Clear()
    {
        _queue.Clear();
        _shown = -1;
        InvalidateVisual();
    }

    private void Wake()
    {
        System.Threading.Interlocked.Exchange(ref _wakeQueued, 0);
        RequestFrame();
    }

    private void RequestFrame()
    {
        if (_isFrameRequested || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        _isFrameRequested = true;
        topLevel.RequestAnimationFrame(OnFrame);
    }

    // On every refresh while frames wait
    private void OnFrame(TimeSpan time)
    {
        _isFrameRequested = false;

        var frame = _queue.Pick();

        if (frame != null)
        {
            try
            {
                Show(frame);
            }
            finally
            {
                frame.Release();
            }
        }

        if (_queue.HasPending)
            RequestFrame();
    }

    private unsafe void Show(PendingFrame frame)
    {
        if (frame.Pixels == null)
            return;

        var index = _shown == 0 ? 1 : 0;
        var bitmap = _bitmaps[index];

        if (bitmap == null || bitmap.PixelSize.Width != frame.Width || bitmap.PixelSize.Height != frame.Height)
        {
            bitmap?.Dispose();
            bitmap = _bitmaps[index] = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
        }

        using (var buffer = bitmap.Lock())
        {
            fixed (byte* source = frame.Pixels)
            {
                for (var row = 0; row < frame.Height; row++)
                    Buffer.MemoryCopy(source + (long)row * frame.Stride, (byte*)buffer.Address + (long)row * buffer.RowBytes,
                        buffer.RowBytes, Math.Min(frame.Stride, buffer.RowBytes));
            }
        }

        _shown = index;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_shown < 0 || _bitmaps[_shown] is not { } bitmap)
            return;

        var size = bitmap.PixelSize;
        context.DrawImage(bitmap, new Rect(0, 0, size.Width, size.Height),
            VideoLayout.Fit(new Rect(Bounds.Size), size.Width, size.Height, Stretch));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _queue.Clear();
        _shown = -1;

        for (var index = 0; index < _bitmaps.Length; index++)
        {
            _bitmaps[index]?.Dispose();
            _bitmaps[index] = null;
        }
    }
}
