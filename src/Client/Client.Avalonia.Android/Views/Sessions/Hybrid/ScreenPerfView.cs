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

    private readonly StreamPerfOverlay _overlay = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(6)
    };

    // The stream the statistics come from now
    private ScreenStreamVM? _attached;

    public ScreenPerfView()
    {
        Children.Add(_overlay);
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
            _overlay.IsExpandedTile = IsFullscreen;
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
