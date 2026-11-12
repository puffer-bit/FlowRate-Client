using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Client.Core.Media;

namespace Client.Avalonia.Video;

/// <summary>
/// Shows frames with OpenGL on the render thread of the compositor, see <see cref="GlVideoRenderer"/>:
/// the UI thread only places the visual, so whatever it is busy with does not hold a frame back.
/// </summary>
public class GlVideoPresenter : Control, IVideoPresenter
{
    private readonly VideoFrameQueue _queue = new();
    private CompositionCustomVisual? _visual;
    private volatile GlVideoRenderer? _renderer;
    private int _wakeQueued;

    // OpenGL turned out not to be usable; frames go to another presenter from now on
    private volatile bool _isBroken;

    public static readonly StyledProperty<StretchMode> StretchProperty =
        AvaloniaProperty.Register<GlVideoPresenter, StretchMode>(nameof(Stretch), StretchMode.Uniform);

    public StretchMode Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public FrameStats Stats => _queue.Stats;

    public bool PushFrame(RawVideoFrame frame)
    {
        if (_isBroken || !_queue.Enqueue(frame))
            return false;

        // Coalesce arrivals until the UI thread can notify the compositor.
        if (_renderer is { } renderer && renderer.TryQueueWake() &&
            System.Threading.Interlocked.Exchange(ref _wakeQueued, 1) == 0)
            Dispatcher.UIThread.Post(Wake);

        return true;
    }

    private void Wake()
    {
        System.Threading.Interlocked.Exchange(ref _wakeQueued, 0);
        _visual?.SendHandlerMessage(new GlVideoRenderer.FrameAvailableMessage());
    }

    public void Clear()
    {
        _queue.Clear();
        _visual?.SendHandlerMessage(new GlVideoRenderer.ClearMessage());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (ElementComposition.GetElementVisual(this)?.Compositor is not { } compositor)
            return;

        var renderer = new GlVideoRenderer(_queue, OnBroken);
        _renderer = renderer;
        _visual = compositor.CreateCustomVisual(renderer);
        _visual.Size = new Vector(Bounds.Width, Bounds.Height);
        ElementComposition.SetElementChildVisual(this, _visual);
        _visual.SendHandlerMessage(new GlVideoRenderer.StretchMessage(Stretch));
        _visual.SendHandlerMessage(new GlVideoRenderer.StartMessage());
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _renderer = null;
        _queue.Clear();
        _visual?.SendHandlerMessage(new GlVideoRenderer.StopMessage());
        ElementComposition.SetElementChildVisual(this, null);
        _visual = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty && _visual != null)
            _visual.Size = new Vector(Bounds.Width, Bounds.Height);
        else if (change.Property == StretchProperty)
            _visual?.SendHandlerMessage(new GlVideoRenderer.StretchMessage(Stretch));
    }

    // On the render thread
    private void OnBroken()
    {
        _isBroken = true;
        _queue.Clear();

        // Every kind needs this presenter, so streams that deliver one are rebuilt for the bitmap
        VideoSurfaceSupport.ReportFailure(VideoSurfaceSupport.Kinds);
        Dispatcher.UIThread.Post(() => IsVisible = false);
    }
}
