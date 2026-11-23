using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Client.Avalonia.Controls.Media;
using Client.Core.Media;
using Client.ViewModels.Sessions.Hybrid;

namespace Client.Droid.Views.Sessions.Hybrid;

/// <summary>The statistics of a screen over its picture, fed by the screen while the view is on screen.</summary>
public sealed class ScreenPerfView : Panel
{
    public static readonly StyledProperty<ScreenStreamVM?> StreamProperty =
        AvaloniaProperty.Register<ScreenPerfView, ScreenStreamVM?>(nameof(Stream));

    public static readonly StyledProperty<bool> IsFullscreenProperty =
        AvaloniaProperty.Register<ScreenPerfView, bool>(nameof(IsFullscreen));

    // Over the whole display the statistics start below the button that closes it
    private static readonly Thickness TileMargin = new(6);
    private static readonly Thickness FullscreenMargin = new(6, 64, 6, 6);

    private readonly StreamPerfOverlay _overlay = new();

    // The extended statistics can be taller than the picture: they scroll instead of being cut
    private readonly ScrollViewer _scroller = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Bottom,
        HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        Margin = TileMargin
    };

    // The stream the statistics come from now
    private ScreenStreamVM? _attached;

    public ScreenPerfView()
    {
        _scroller.Content = _overlay;
        Children.Add(_scroller);
    }

    public ScreenStreamVM? Stream
    {
        get => GetValue(StreamProperty);
        set => SetValue(StreamProperty, value);
    }

    /// <summary>The picture fills the display, for the setting that shows them only then.</summary>
    public bool IsFullscreen
    {
        get => GetValue(IsFullscreenProperty);
        set => SetValue(IsFullscreenProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StreamProperty && VisualRoot != null)
            Attach(Stream);
        else if (change.Property == IsFullscreenProperty)
        {
            _overlay.IsExpandedTile = IsFullscreen;
            _scroller.Margin = IsFullscreen ? FullscreenMargin : TileMargin;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Attach(Stream);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Attach(null);
    }

    // The stream outlives the view, and would keep it otherwise
    private void Attach(ScreenStreamVM? stream)
    {
        if (ReferenceEquals(_attached, stream))
            return;

        if (_attached != null)
        {
            _attached.PerfChanged -= _overlay.SetPerf;
            _overlay.ReadFrames = null;
        }

        _attached = stream;
        _overlay.Reset();

        if (stream == null)
            return;

        stream.PerfChanged += _overlay.SetPerf;
        _overlay.ReadFrames = () => stream.ReadFrameStats?.Invoke();
        _overlay.SetPerf(stream.Perf.Stream, stream.Perf.Network);
    }
}
