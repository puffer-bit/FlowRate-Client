using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Client.Avalonia.Localization;
using Client.Core.Media;
using Client.Core.Settings;

namespace Client.Avalonia.Video;

/// <summary>
/// Shows the frames of one stream with the fastest presenter this system has, falling back to a
/// bitmap for frames that presenter cannot draw, and the frame counter when the settings ask.
/// </summary>
public class VideoView : Panel
{
    private static readonly TimeSpan StatsInterval = TimeSpan.FromMilliseconds(500);

    private readonly IVideoPresenter? _fast;
    private readonly BitmapVideoPresenter _bitmap = new() { IsVisible = false };
    private readonly Border _statsBox;
    private readonly TextBlock _statsText;
    private readonly TextBlock _routeText;
    private readonly TextBlock _fallbackText;
    private readonly DispatcherTimer _statsTimer;

    // Which presenter got the last frame; set on the thread of the stream
    private volatile IVideoPresenter? _active;

    // The form of the last frame and how its decoder decodes, for the counter
    private volatile VideoSurfaceKinds _surface;
    private volatile VideoDecoding? _decoding;

    // Whether the route is drawn as a path without copies, so its colour changes only when that does
    private bool? _isRouteZeroCopy;

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

        _statsText = new TextBlock { FontSize = 11 };
        _statsText.Bind(TextBlock.ForegroundProperty, _statsText.GetResourceObservable("PrimaryTextBrush"));
        _routeText = new TextBlock { FontSize = 11 };
        _fallbackText = new TextBlock { FontSize = 11, IsVisible = false, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
        _fallbackText.Bind(TextBlock.ForegroundProperty, _fallbackText.GetResourceObservable("WarningBrush"));
        _statsBox = new Border
        {
            Child = new StackPanel { Children = { _statsText, _routeText, _fallbackText } },
            Padding = new Thickness(6, 2),
            Margin = new Thickness(6),
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsVisible = false,
            IsHitTestVisible = false
        };
        _statsBox.Bind(Border.BackgroundProperty, _statsBox.GetResourceObservable("GeneralBrush"));
        Children.Add(_statsBox);

        _statsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = StatsInterval };
        _statsTimer.Tick += (_, _) => UpdateStats();
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

    /// <summary>Tells the counter how the stream is decoded; any thread.</summary>
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _statsTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _statsTimer.Stop();
    }

    private void UpdateStats()
    {
        var isShown = DebugSettings.Current.ShowFrameStats && _active != null;
        _statsBox.IsVisible = isShown;

        if (!isShown || _active is not { } active)
            return;

        var stats = active.Stats;

        _statsText.Text = Strings.Format("hybrid.screen.stats.frames",
            ("shown", stats.ShownPerSecond.ToString("0", CultureInfo.InvariantCulture)),
            ("received", stats.ReceivedPerSecond.ToString("0", CultureInfo.InvariantCulture)),
            ("dropped", stats.Dropped));

        // Who decodes, then the way to the screen: the form of the frames that really arrive, and
        // whether the bitmap has to copy them. The preview of one's own screen has no decoder, and its
        // way says nothing of how watched screens are shown
        var decoding = _decoding;
        _routeText.IsVisible = decoding != null;
        _fallbackText.IsVisible = false;

        if (decoding == null)
            return;

        var surface = _surface;
        var isZeroCopy = surface is VideoSurfaceKinds.DmaBuf or VideoSurfaceKinds.CudaGl or VideoSurfaceKinds.D3D11Shared;
        var route = isZeroCopy
            ? Strings.Format("hybrid.screen.stats.zeroCopy", ("way", string.Join(", ", VideoElementNames.Of(surface))))
            : Strings.Get(ReferenceEquals(active, _bitmap) ? "hybrid.screen.stats.memoryBitmap" : "hybrid.screen.stats.memory");

        _routeText.Text = Strings.Format(decoding.IsHardware ? "hybrid.screen.stats.gpu" : "hybrid.screen.stats.cpu",
            ("name", VideoElementNames.Of(decoding.Decoder)), ("route", route));

        if (_isRouteZeroCopy != isZeroCopy)
        {
            _isRouteZeroCopy = isZeroCopy;
            _routeText.Bind(TextBlock.ForegroundProperty,
                _routeText.GetResourceObservable(isZeroCopy ? "SuccessBrush" : "PrimaryTextBrush"));
        }

        var failed = decoding.FailedSurface;
        _fallbackText.IsVisible = VideoElementNames.Of(failed).Count > 0;

        if (_fallbackText.IsVisible)
            _fallbackText.Text = Strings.Format("hybrid.screen.stats.fallback",
                ("way", string.Join(", ", VideoElementNames.Of(failed))),
                ("reason", decoding.FailureReason ?? Strings.Get("hybrid.screen.stats.noReason")));
    }
}
