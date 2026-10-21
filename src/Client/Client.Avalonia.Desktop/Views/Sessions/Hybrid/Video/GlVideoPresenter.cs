using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Client.Core.Media;

namespace Client.Views.Sessions.Hybrid.Video;

/// <summary>
/// Shows frames with OpenGL on the render thread of the compositor, see <see cref="GlVideoRenderer"/>:
/// the UI thread only places the visual, so whatever it is busy with does not hold a frame back.
/// </summary>
public class GlVideoPresenter : Control, IVideoPresenter
{
    private readonly VideoFrameQueue _queue = new();
    private CompositionCustomVisual? _visual;

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

    public bool PushFrame(RawVideoFrame frame) => !_isBroken && _queue.Enqueue(frame);

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

        _visual = compositor.CreateCustomVisual(new GlVideoRenderer(_queue, OnBroken));
        _visual.Size = new Vector(Bounds.Width, Bounds.Height);
        ElementComposition.SetElementChildVisual(this, _visual);
        _visual.SendHandlerMessage(new GlVideoRenderer.StretchMessage(Stretch));
        _visual.SendHandlerMessage(new GlVideoRenderer.StartMessage());
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

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
        Dispatcher.UIThread.Post(() => IsVisible = false);
    }
}
