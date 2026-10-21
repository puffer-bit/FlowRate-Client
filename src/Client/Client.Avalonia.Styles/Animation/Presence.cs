using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using KeyframeAnimation = global::Avalonia.Animation.Animation;

namespace Client.Avalonia.Styles.Animation;

/// <summary>
/// How an element comes into view.
/// </summary>
public enum AppearEffect
{
    None,

    /// <summary>Fades in.</summary>
    Fade,

    /// <summary>Fades in while rising into its place from slightly below.</summary>
    Rise,

    /// <summary>Fades in while dropping into its place from slightly above.</summary>
    Drop,

    /// <summary>Fades in while growing from slightly smaller.</summary>
    Grow,

    /// <summary>A row unfolds its height while it fades and slides in, so the rows below slide down.</summary>
    Unfold
}

/// <summary>
/// How an element goes out of view.
/// </summary>
public enum LeaveEffect
{
    None,

    /// <summary>Fades out.</summary>
    Fade,

    /// <summary>Fades out while shrinking a little, the reverse of <see cref="AppearEffect.Grow"/>.</summary>
    Shrink,

    /// <summary>A row fades out and folds its height to zero, so the rows below slide up.</summary>
    Fold
}

/// <summary>
/// The one place where elements are animated in and out of view. Markup, or a style of generated
/// containers, names the effect: <c>anim:Presence.Appear="Rise"</c>. The appear effect is played
/// each time the element is shown: it enters the visual tree or becomes visible.
/// <para>
/// An element cannot animate its own removal, so leaving takes two steps: the view model sets a flag
/// bound to <see cref="IsLeavingProperty"/>, waits <see cref="LeaveTime"/> and only then removes
/// the item. Until then the element stays faded and takes no pointer input.
/// </para>
/// </summary>
public static class Presence
{
    // How far an element moves while it appears
    private const double RiseOffset = 8;
    private const double DropOffset = -6;
    private const double UnfoldOffset = -8;
    // What an element is scaled to at the far end of an effect
    private const double GrowScale = 0.92;

    public static readonly AttachedProperty<AppearEffect> AppearProperty =
        AvaloniaProperty.RegisterAttached<Control, AppearEffect>("Appear", typeof(Presence));

    public static readonly AttachedProperty<LeaveEffect> LeaveProperty =
        AvaloniaProperty.RegisterAttached<Control, LeaveEffect>("Leave", typeof(Presence));

    public static readonly AttachedProperty<bool> IsLeavingProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsLeaving", typeof(Presence));

    /// <summary>
    /// Replaces the length an effect has by itself, for both directions.
    /// </summary>
    public static readonly AttachedProperty<MotionDuration?> DurationProperty =
        AvaloniaProperty.RegisterAttached<Control, MotionDuration?>("Duration", typeof(Presence));

    // The effect that is being played on the control, to stop it when another one starts
    private static readonly AttachedProperty<CancellationTokenSource?> RunProperty =
        AvaloniaProperty.RegisterAttached<Control, CancellationTokenSource?>("Run", typeof(Presence));

    private static readonly AttachedProperty<bool> IsAppearQueuedProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsAppearQueued", typeof(Presence));

    static Presence()
    {
        AppearProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            control.AttachedToVisualTree -= OnAttachedToVisualTree;

            if (e.NewValue is AppearEffect.None)
                return;

            control.AttachedToVisualTree += OnAttachedToVisualTree;

            // A style names the effect only after its control has entered the tree
            if (e.OldValue is AppearEffect.None)
                QueueAppear(control);
        });

        Visual.IsVisibleProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.NewValue is true && GetAppear(control) != AppearEffect.None)
                QueueAppear(control);
        });

        IsLeavingProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.NewValue is true)
                StartLeave(control);
            else
                Reset(control);
        });
    }

    public static AppearEffect GetAppear(Control control) => control.GetValue(AppearProperty);
    public static void SetAppear(Control control, AppearEffect value) => control.SetValue(AppearProperty, value);

    public static LeaveEffect GetLeave(Control control) => control.GetValue(LeaveProperty);
    public static void SetLeave(Control control, LeaveEffect value) => control.SetValue(LeaveProperty, value);

    public static bool GetIsLeaving(Control control) => control.GetValue(IsLeavingProperty);
    public static void SetIsLeaving(Control control, bool value) => control.SetValue(IsLeavingProperty, value);

    public static MotionDuration? GetDuration(Control control) => control.GetValue(DurationProperty);
    public static void SetDuration(Control control, MotionDuration? value) => control.SetValue(DurationProperty, value);

    /// <summary>
    /// How long a leaving element is to be kept before it is removed.
    /// </summary>
    public static TimeSpan LeaveTime => Motion.Get(MotionDuration.Medium);

    /// <summary>
    /// The same for an element with a <see cref="DurationProperty"/> of its own.
    /// </summary>
    public static TimeSpan GetLeaveTime(Control control) => Motion.Get(GetDuration(control) ?? MotionDuration.Medium);

    /// <summary>
    /// Plays an appear effect once, for an element that is not animated every time it is shown.
    /// </summary>
    public static void Play(Control control, AppearEffect effect)
    {
        if (effect == AppearEffect.None || !Motion.IsEnabled)
            return;

        Run(control, BuildAppear(control, effect));
    }

    private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
            QueueAppear(control);
    }

    // Entering the tree, being styled and being shown can all happen in one go and count as one
    // appearance. The effect starts after them and before the next frame, so that the element is
    // never drawn in its final state first
    private static void QueueAppear(Control control)
    {
        if (control.GetValue(IsAppearQueuedProperty))
            return;

        control.SetValue(IsAppearQueuedProperty, true);
        Dispatcher.UIThread.Post(() =>
        {
            control.SetValue(IsAppearQueuedProperty, false);

            if (!control.IsVisible || GetIsLeaving(control) || TopLevel.GetTopLevel(control) == null)
                return;

            Play(control, GetAppear(control));
        }, DispatcherPriority.Send);
    }

    private static void StartLeave(Control control)
    {
        control.IsHitTestVisible = false;

        var effect = GetLeave(control);
        if (effect == LeaveEffect.None || !Motion.IsEnabled)
            return;

        Run(control, BuildLeave(control, effect));
    }

    // A recycled view gets the next item, which is not leaving, so everything a leave effect has
    // left behind goes back to normal
    private static void Reset(Control control)
    {
        Stop(control);

        control.ClearValue(InputElement.IsHitTestVisibleProperty);
        control.ClearValue(Visual.OpacityProperty);

        switch (GetLeave(control))
        {
            case LeaveEffect.Fold:
                control.ClearValue(Layoutable.MaxHeightProperty);
                control.ClearValue(Visual.ClipToBoundsProperty);
                break;
            case LeaveEffect.Shrink:
                control.ClearValue(Visual.RenderTransformProperty);
                break;
        }
    }

    private static void Run(Control control, KeyframeAnimation animation)
    {
        Stop(control);

        var run = new CancellationTokenSource();
        control.SetValue(RunProperty, run);
        _ = animation.RunAsync(control, run.Token);
    }

    private static void Stop(Control control)
    {
        if (control.GetValue(RunProperty) is not { } run)
            return;

        control.SetValue(RunProperty, null);
        run.Cancel();
        run.Dispose();
    }

    // Keyframes animate TranslateTransform and ScaleTransform properties: Avalonia has no keyframe
    // animator for RenderTransform itself and builds the transforms on a control that has none
    private static KeyframeAnimation BuildAppear(Control control, AppearEffect effect)
    {
        var duration = GetDuration(control);

        switch (effect)
        {
            case AppearEffect.Rise:
                return Build(duration ?? MotionDuration.Long, MotionEase.Enter, FillMode.None,
                    [Opacity(0), Set(TranslateTransform.YProperty, RiseOffset)],
                    [Opacity(1), Set(TranslateTransform.YProperty, 0)]);

            case AppearEffect.Drop:
                return Build(duration ?? MotionDuration.Long, MotionEase.Enter, FillMode.None,
                    [Opacity(0), Set(TranslateTransform.YProperty, DropOffset)],
                    [Opacity(1), Set(TranslateTransform.YProperty, 0)]);

            case AppearEffect.Grow:
                return Build(duration ?? MotionDuration.Long, MotionEase.Enter, FillMode.None,
                    [Opacity(0), ..Scale(GrowScale)],
                    [Opacity(1), ..Scale(1)]);

            case AppearEffect.Unfold when MeasureHeight(control) is var height and > 0:
                return Build(duration ?? MotionDuration.Long, MotionEase.Enter, FillMode.None,
                    [Opacity(0), Set(Layoutable.MaxHeightProperty, 0), Set(TranslateTransform.XProperty, UnfoldOffset)],
                    [Opacity(1), Set(Layoutable.MaxHeightProperty, height), Set(TranslateTransform.XProperty, 0)]);

            // Also a row whose height is not known: it cannot unfold, so it only fades in
            default:
                return Build(duration ?? MotionDuration.Medium, MotionEase.Enter, FillMode.None,
                    [Opacity(0)],
                    [Opacity(1)]);
        }
    }

    // A leave effect holds its last frame: the element stays out of view until it is removed
    private static KeyframeAnimation BuildLeave(Control control, LeaveEffect effect)
    {
        var duration = GetDuration(control) ?? MotionDuration.Medium;
        // An element that is still coming in leaves from where it has got to
        var opacity = control.Opacity;

        switch (effect)
        {
            case LeaveEffect.Shrink:
                return Build(duration, MotionEase.Exit, FillMode.Forward,
                    [Opacity(opacity), ..Scale(1)],
                    [Opacity(0), ..Scale(GrowScale)]);

            case LeaveEffect.Fold:
                // The row is cut, not squeezed over its neighbours, while its height folds
                control.ClipToBounds = true;
                return Build(duration, MotionEase.Standard, FillMode.Forward,
                    [Opacity(opacity), Set(Layoutable.MaxHeightProperty, control.Bounds.Height)],
                    [Opacity(0), Set(Layoutable.MaxHeightProperty, 0)]);

            default:
                return Build(duration, MotionEase.Exit, FillMode.Forward,
                    [Opacity(opacity)],
                    [Opacity(0)]);
        }
    }

    private static KeyframeAnimation Build(MotionDuration duration, MotionEase ease, FillMode fillMode,
        Setter[] from, Setter[] to)
    {
        var first = new KeyFrame { Cue = new Cue(0) };
        first.Setters.AddRange(from);

        var last = new KeyFrame { Cue = new Cue(1) };
        last.Setters.AddRange(to);

        return new KeyframeAnimation
        {
            Duration = Motion.Get(duration),
            Easing = Motion.Ease(ease),
            FillMode = fillMode,
            Children = { first, last }
        };
    }

    // The height the row is going to take, without its margin. It has not been laid out yet
    private static double MeasureHeight(Control control)
    {
        control.Measure(Size.Infinity);

        var margin = control.Margin;
        return control.DesiredSize.Height - margin.Top - margin.Bottom;
    }

    private static Setter Opacity(double value) => Set(Visual.OpacityProperty, value);

    private static Setter[] Scale(double value) =>
        [Set(ScaleTransform.ScaleXProperty, value), Set(ScaleTransform.ScaleYProperty, value)];

    private static Setter Set(AvaloniaProperty property, double value) => new(property, value);
}
