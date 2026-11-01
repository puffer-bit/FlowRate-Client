using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Client.Avalonia.Localization;
using Client.Core.Media;

namespace Client.Avalonia.Video;

/// <summary>
/// Shows the frames of one stream with the fastest presenter this system has, falling back to a
/// bitmap for frames that presenter cannot draw. Tells the statistics of the screen how its frames are
/// drawn.
/// </summary>
public class VideoView : Panel
{
    private readonly IVideoPresenter? _fast;
    private readonly BitmapVideoPresenter _bitmap = new() { IsVisible = false };

    // Which presenter got the last frame; set on the thread of the stream
    private volatile IVideoPresenter? _active;

    // The form of the last frame and how its decoder decodes, for the statistics
    private volatile VideoSurfaceKinds _surface;
    private volatile VideoDecoding? _decoding;

    public static readonly StyledProperty<StretchMode> StretchProperty =
        AvaloniaProperty.Register<VideoView, StretchMode>(nameof(Stretch), StretchMode.Uniform);

    public StretchMode Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public VideoView()
    {
        _fast = VideoSurfaceProbe.UsesGl ? new GlVideoPresenter { IsVisible = false } : null;

        if (_fast is Control fast)
            Children.Add(fast);

        Children.Add(_bitmap);

        ApplyStretch();
    }

    /// <summary>Takes a frame on the thread of its stream; it is only valid during the call.</summary>
    public void PushFrame(RawVideoFrame frame)
    {
        IVideoPresenter? presenter = null;

        _surface = frame.Gpu switch
        {
            DmaBufVideoFrame => VideoSurfaceKinds.DmaBuf,
            GlCopyVideoFrame => VideoSurfaceKinds.CudaGl,
            D3D11SharedVideoFrame => VideoSurfaceKinds.D3D11Shared,
            MemoryYuvVideoFrame => VideoSurfaceKinds.YuvMemory,
            _ => VideoSurfaceKinds.None
        };

        if (_fast != null && _fast.PushFrame(frame))
            presenter = _fast;
        else if (_bitmap.PushFrame(frame))
            presenter = _bitmap;

        if (presenter == null || ReferenceEquals(presenter, _active))
            return;

        _active = presenter;
        Dispatcher.UIThread.Post(ShowActive);
    }

    /// <summary>
    /// The counters of the frames since the picture started, or null while nothing draws it.
    /// </summary>
    public FrameStats? Stats => _active?.Stats;

    /// <summary>Tells the statistics how the stream is decoded; any thread.</summary>
    public void SetDecoding(VideoDecoding? decoding) => _decoding = decoding;

    /// <summary>Stops showing the last frame, once the stream it came from is over.</summary>
    public void Clear()
    {
        _fast?.Clear();
        _bitmap.Clear();
    }

    private void ShowActive()
    {
        var active = _active;

        if (_fast is Control fast)
            fast.IsVisible = ReferenceEquals(active, _fast);

        _bitmap.IsVisible = ReferenceEquals(active, _bitmap);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StretchProperty)
            ApplyStretch();
    }

    private void ApplyStretch()
    {
        if (_fast != null)
            _fast.Stretch = Stretch;

        _bitmap.Stretch = Stretch;
    }

    /// <summary>
    /// Who decodes and the way of the frames to the screen: the form of the frames that really arrive,
    /// and whether the bitmap has to copy them; then what failed of the faster ways. Null for the preview
    /// of one's own screen, which has no decoder and whose way says nothing of how watched screens are
    /// shown, and while nothing draws.
    /// </summary>
    public string? Route
    {
        get
        {
            var decoding = _decoding;

            if (decoding == null || _active is not { } active)
                return null;

            var surface = _surface;
            var isZeroCopy = surface is VideoSurfaceKinds.DmaBuf or VideoSurfaceKinds.CudaGl or VideoSurfaceKinds.D3D11Shared;
            var route = isZeroCopy
                ? Strings.Format("hybrid.screen.stats.zeroCopy", ("way", string.Join(", ", VideoElementNames.Of(surface))))
                : Strings.Get(ReferenceEquals(active, _bitmap) ? "hybrid.screen.stats.memoryBitmap" : "hybrid.screen.stats.memory");
            var text = Strings.Format(decoding.IsHardware ? "hybrid.screen.stats.gpu" : "hybrid.screen.stats.cpu",
                ("name", VideoElementNames.Of(decoding.Decoder)), ("route", route));
            var failed = VideoElementNames.Of(decoding.FailedSurface);

            return failed.Count == 0
                ? text
                : text + "\n" + Strings.Format("hybrid.screen.stats.fallback",
                    ("way", string.Join(", ", failed)),
                    ("reason", decoding.FailureReason ?? Strings.Get("hybrid.screen.stats.noReason")));
        }
    }
}
