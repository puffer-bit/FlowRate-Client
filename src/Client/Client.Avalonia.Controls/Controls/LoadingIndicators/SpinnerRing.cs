using System;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Client.Avalonia.Styles.Animation;

namespace Client.Avalonia.Controls.LoadingIndicators;

/// <summary>
/// An arc of a fixed length that turns around a ring. Unlike <see cref="ProgressRing"/>, whose arc is
/// animated by styles on the UI thread, it is turned by the compositor on the render thread, so it keeps
/// turning while the UI thread is busy: the place for it is where such work is expected, as the start of
/// the application.
/// </summary>
public class SpinnerRing : Control
{
    private const string RotationAngle = nameof(CompositionVisual.RotationAngle);
    private const double SweepAngle = 100;
    private static readonly TimeSpan TurnTime = TimeSpan.FromSeconds(1.1);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<SpinnerRing, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<SpinnerRing, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<SpinnerRing, double>(nameof(Thickness), 3);

    private CompositionVisual? _visual;
    private bool _isTurning;

    static SpinnerRing()
    {
        AffectsRender<SpinnerRing>(ForegroundProperty, TrackBrushProperty, ThicknessProperty);
    }

    public SpinnerRing()
    {
        // The compositor would turn a hidden ring for ever: it turns while :shown only
        ShownState.SetTrack(this, true);
        Classes.CollectionChanged += (_, _) => UpdateTurning();
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The whole ring under the arc; none when null.</summary>
    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _visual = ElementComposition.GetElementVisual(this);
        UpdateCenter();
        UpdateTurning();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _visual?.StopAnimation(RotationAngle);
        _visual = null;
        _isTurning = false;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateCenter();
    }

    public override void Render(DrawingContext context)
    {
        var thickness = Thickness;
        var radius = (Math.Min(Bounds.Width, Bounds.Height) - thickness) / 2;
        if (radius <= 0)
            return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);

        if (TrackBrush is { } track)
            context.DrawEllipse(null, new Pen(track, thickness), center, radius, radius);

        if (Foreground is not { } foreground)
            return;

        // From the top, clockwise; the compositor turns it from there
        var end = (SweepAngle - 90) * Math.PI / 180;
        var arc = new StreamGeometry();
        using (var geometry = arc.Open())
        {
            geometry.BeginFigure(new Point(center.X, center.Y - radius), false);
            geometry.ArcTo(new Point(center.X + radius * Math.Cos(end), center.Y + radius * Math.Sin(end)),
                new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            geometry.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(foreground, thickness, lineCap: PenLineCap.Round), arc);
    }

    private void UpdateCenter()
    {
        if (_visual != null)
            _visual.CenterPoint = new Vector3D(Bounds.Width / 2, Bounds.Height / 2, 0);
    }

    private void UpdateTurning()
    {
        var shouldTurn = _visual != null && Classes.Contains(ShownState.PseudoClass);
        if (shouldTurn == _isTurning || _visual == null)
            return;

        _isTurning = shouldTurn;

        if (!shouldTurn)
        {
            _visual.StopAnimation(RotationAngle);
            return;
        }

        var turn = _visual.Compositor.CreateScalarKeyFrameAnimation();
        turn.InsertKeyFrame(0f, 0f, new LinearEasing());
        turn.InsertKeyFrame(1f, (float)(2 * Math.PI), new LinearEasing());
        turn.Duration = TurnTime;
        turn.IterationBehavior = global::Avalonia.Rendering.Composition.Animations.AnimationIterationBehavior.Forever;
        _visual.StartAnimation(RotationAngle, turn);
    }
}
