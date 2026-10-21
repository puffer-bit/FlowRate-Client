using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Client.Avalonia.Styles.Animation;

namespace Client.Avalonia.Controls.Panels;

/// <summary>
/// A <see cref="UniformGrid"/> whose cells glide to their new place and size when the grid is laid
/// out anew: a child was added, removed, shown or hidden. A cell is put where the layout wants it
/// at once and is only drawn at its old place, from which it then moves over; the layout itself
/// is never in between. Resizing the grid is not animated, the cells follow it as they are.
/// </summary>
public class AnimatedUniformGrid : UniformGrid
{
    private static readonly TransformOperations AtRest = Operations(Matrix.Identity);

    // Where the visible cells were put by the previous layout pass
    private Dictionary<Control, Rect> _arranged = new();
    private Size _arrangedSize;

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);

        // A grid that changed its own size is being resized, and gliding after every step of that lags behind it
        var isRearranged = finalSize == _arrangedSize;
        _arrangedSize = finalSize;

        var arranged = new Dictionary<Control, Rect>(Children.Count);

        foreach (var child in Children)
        {
            // A hidden cell is left out: when it is shown again it has no place to come from
            if (!child.IsVisible)
                continue;

            var bounds = child.Bounds;
            arranged[child] = bounds;

            if (isRearranged && _arranged.TryGetValue(child, out var previous) && previous != bounds)
                Glide(child, previous, bounds);
        }

        _arranged = arranged;
        return size;
    }

    private void Glide(Control child, Rect from, Rect to)
    {
        if (!Motion.IsEnabled || from.Width <= 0 || from.Height <= 0 || to.Width <= 0 || to.Height <= 0)
            return;

        // The render transform is applied around the center of the cell: scaled to the old size
        // there, the cell is then moved onto the old center
        var start = Matrix.CreateScale(from.Width / to.Width, from.Height / to.Height) *
                    Matrix.CreateTranslation(from.Center.X - to.Center.X, from.Center.Y - to.Center.Y);

        var transitions = child.Transitions ??= new Transitions();

        // Taken out for the jump to the old place, which is not to be animated. It also stops a glide that is still under way
        for (var i = transitions.Count - 1; i >= 0; i--)
        {
            if (transitions[i] is TransformOperationsTransition transition && transition.Property == RenderTransformProperty)
                transitions.RemoveAt(i);
        }

        child.RenderTransform = Operations(start);

        transitions.Add(new TransformOperationsTransition
        {
            Property = RenderTransformProperty,
            Duration = Motion.Get(MotionDuration.Long),
            Easing = Motion.Ease(MotionEase.Enter)
        });

        child.RenderTransform = AtRest;
    }

    // Always one matrix, so that the start and the end of a glide are of the same kind and can be blended
    private static TransformOperations Operations(Matrix matrix)
    {
        var builder = TransformOperations.CreateBuilder(1);
        builder.AppendMatrix(matrix);
        return builder.Build();
    }
}
