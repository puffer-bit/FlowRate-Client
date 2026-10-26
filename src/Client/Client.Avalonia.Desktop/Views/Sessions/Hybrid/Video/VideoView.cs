using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Client.Core.Media;
using Client.Core.Settings;

namespace Client.Views.Sessions.Hybrid.Video;

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
    private readonly DispatcherTimer _statsTimer;

    // Which presenter got the last frame; set on the thread of the stream
    private volatile IVideoPresenter? _active;

    // Where the last frame came from, for the counter
    private volatile string _source = "";

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
        _statsBox = new Border
        {
            Child = _statsText,
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

        _source = frame.Gpu switch
        {
            DmaBufVideoFrame => "DMA-BUF",
            GlCopyVideoFrame => "CUDA",
            D3D11SharedVideoFrame => "D3D11",
            MemoryYuvVideoFrame => "memory YUV",
            _ => "memory"
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

    /// <summary>Stops showing the last frame, once the stream it came from is over.</summary>
    /// <summary>
    /// The counters of the frames since the picture started, or null while nothing draws it.
    /// </summary>
    public FrameStats? Stats => _active?.Stats;

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
        var isShown = VideoSettings.Current.ShowFrameStats && _active != null;
        _statsBox.IsVisible = isShown;

        if (!isShown || _active is not { } active)
            return;

        var stats = active.Stats;
        var presenter = ReferenceEquals(active, _bitmap) ? "bitmap" : "OpenGL";

        // Shown / received, then where the frames come from and what draws them
        _statsText.Text = string.Format(CultureInfo.InvariantCulture,
            "{0:0} / {1:0} fps · {2} dropped · {3} → {4}", stats.ShownPerSecond, stats.ReceivedPerSecond,
            stats.Dropped, _source, presenter);
    }
}
