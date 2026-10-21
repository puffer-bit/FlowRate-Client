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

    public HybridScreenTile()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        Attach(DataContext as ScreenStreamVM);
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
        }

        _stream = stream;
        Picture.Clear();

        if (stream == null)
            return;

        stream.FrameReady += OnFrame;
        stream.Cleared += Picture.Clear;
    }

    private void OnPictureDoubleTapped(object? sender, TappedEventArgs e)
    {
        _stream?.ToggleExpandCommand.Execute().Subscribe();
        e.Handled = true;
    }

    // Raised on a thread of the track, and the pixels are only valid during the call
    private void OnFrame(RawVideoFrame frame) => Picture.PushFrame(frame);
}
