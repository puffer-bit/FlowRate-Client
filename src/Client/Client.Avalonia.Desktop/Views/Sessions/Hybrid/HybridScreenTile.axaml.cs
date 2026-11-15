using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Client.Core.Media;
using Client.ViewModels.Sessions.Hybrid;

namespace Client.Views.Sessions.Hybrid;

public partial class HybridScreenTile : UserControl
{
    // The stream whose frames are drawn here; the tile is reused for another one when the list changes
    private ScreenStreamVM? _stream;

    private const int PictureHeightStep = 180;

    // What the stream was last told, in pixels
    private int _reportedHeight;

    public HybridScreenTile()
    {
        InitializeComponent();

        Perf.ReadFrames = () => Picture.Stats;
        Perf.ReadRoute = () => Picture.Route;

        Picture.PropertyChanged += (_, change) =>
        {
            if (change.Property == BoundsProperty)
                ReportPictureHeight();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        Attach(DataContext as ScreenStreamVM);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);

        // Pointer focus must not pin the bar open after clicking a control.
        Classes.Set("keyboardControls",
            e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);

        if (!IsKeyboardFocusWithin)
            Classes.Set("keyboardControls", false);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        Attach(DataContext as ScreenStreamVM);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // The stream outlives the tile, and would keep it and its bitmap otherwise
        Attach(null);
    }

    private void Attach(ScreenStreamVM? stream)
    {
        if (ReferenceEquals(_stream, stream))
            return;

        if (_stream != null)
        {
            _stream.FrameReady -= OnFrame;
            _stream.Cleared -= Picture.Clear;
            _stream.DecodingChanged -= Picture.SetDecoding;
            _stream.PerfChanged -= Perf.SetPerf;
            _stream.ReadFrameStats = null;
        }

        _stream = stream;
        _reportedHeight = 0;
        Picture.Clear();
        Picture.SetDecoding(null);
        Perf.Reset();

        if (stream == null)
            return;

        stream.FrameReady += OnFrame;
        stream.Cleared += Picture.Clear;
        stream.DecodingChanged += Picture.SetDecoding;
        Picture.SetDecoding(stream.Decoding);
        stream.ReadFrameStats = () => Picture.Stats;
        stream.PerfChanged += Perf.SetPerf;
        Perf.SetPerf(stream.Perf.Stream, stream.Perf.Network);
        ReportPictureHeight();
    }

    private void OnPictureDoubleTapped(object? sender, TappedEventArgs e)
    {
        _stream?.ToggleExpandCommand.Execute().Subscribe();
        e.Handled = true;
    }

    // The preview of our own screen is made as high as it is drawn. Rounded up to a step, so that a
    // window that is being resized does not have the preview made anew at every pixel
    private void ReportPictureHeight()
    {
        if (_stream?.PictureHeightChanged is not { } report || Picture.Bounds.Height <= 0)
            return;

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var height = (int)Math.Ceiling(Picture.Bounds.Height * scaling / PictureHeightStep) * PictureHeightStep;

        if (height == _reportedHeight)
            return;

        _reportedHeight = height;
        report(height);
    }

    // Raised on a thread of the track, and the pixels are only valid during the call
    private void OnFrame(RawVideoFrame frame) => Picture.PushFrame(frame);
}
