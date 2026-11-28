using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using Client.Avalonia.Styles.Animation;

namespace Client.Views.Main;

/// <summary>
/// A row of a list that folds its height down to nothing while it fades out, so the rows under it move
/// up smoothly instead of jumping once it is taken off the list.
/// </summary>
public class CollapsingRow : Panel
{
    public static readonly StyledProperty<bool> IsCollapsedProperty =
        AvaloniaProperty.Register<CollapsingRow, bool>(nameof(IsCollapsed));

    public bool IsCollapsed
    {
        get => GetValue(IsCollapsedProperty);
        set => SetValue(IsCollapsedProperty, value);
    }

    public CollapsingRow()
    {
        ClipToBounds = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCollapsedProperty && change.GetNewValue<bool>())
            Collapse();
    }

    // Once collapsed the row only waits to be taken off the list: it never unfolds again
    private void Collapse()
    {
        var height = Bounds.Height;
        if (height <= 0)
            return;

        _ = new Animation
        {
            Duration = Motion.Get(MotionDuration.Medium),
            Easing = Motion.Ease(MotionEase.Standard),
            FillMode = FillMode.Forward,
            Children =
            {
                Frame(0, height, 1),
                Frame(1, 0, 0)
            }
        }.RunAsync(this);
    }

    private static KeyFrame Frame(double cue, double height, double opacity) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(Layoutable.MaxHeightProperty, height), new Setter(Visual.OpacityProperty, opacity) }
    };
}
