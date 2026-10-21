using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Client.Avalonia.Styles.Animation;

/// <summary>
/// Gives a control the <c>:shown</c> pseudo-class while it is in the visual tree and it and all its
/// ancestors are visible. Style animations keep ticking on hidden controls, so an endless one is
/// written for <c>:shown</c> only: <c>anim:ShownState.Track="True"</c> on the control and
/// <c>Selector="Image.spinning:shown"</c> in the style.
/// </summary>
public static class ShownState
{
    public const string PseudoClass = ":shown";

    public static readonly AttachedProperty<bool> TrackProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Track", typeof(ShownState));

    // Tracked controls that are in the visual tree now
    private static readonly HashSet<Control> Attached = new();

    static ShownState()
    {
        TrackProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            control.AttachedToVisualTree -= OnAttachedToVisualTree;
            control.DetachedFromVisualTree -= OnDetachedFromVisualTree;
            Detach(control);

            if (e.NewValue is not true)
                return;

            control.AttachedToVisualTree += OnAttachedToVisualTree;
            control.DetachedFromVisualTree += OnDetachedFromVisualTree;
            if (TopLevel.GetTopLevel(control) != null)
                Attach(control);
        });

        // One handler for the whole application instead of a subscription to every ancestor
        Visual.IsVisibleProperty.Changed.AddClassHandler<Visual>((visual, _) =>
        {
            if (Attached.Count == 0)
                return;

            foreach (var control in Attached)
            {
                if (visual == control || visual.IsVisualAncestorOf(control))
                    Update(control);
            }
        });
    }

    public static bool GetTrack(Control control) => control.GetValue(TrackProperty);
    public static void SetTrack(Control control, bool value) => control.SetValue(TrackProperty, value);

    private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => Attach((Control)sender!);

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => Detach((Control)sender!);

    private static void Attach(Control control)
    {
        Attached.Add(control);
        Update(control);
    }

    private static void Detach(Control control)
    {
        Attached.Remove(control);
        ((IPseudoClasses)control.Classes).Set(PseudoClass, false);
    }

    private static void Update(Control control)
    {
        var shown = true;
        for (Visual? visual = control; visual != null && shown; visual = visual.GetVisualParent())
            shown = visual.IsVisible;
        ((IPseudoClasses)control.Classes).Set(PseudoClass, shown);
    }
}
