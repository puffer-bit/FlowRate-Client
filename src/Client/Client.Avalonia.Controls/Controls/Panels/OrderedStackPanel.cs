using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace Client.Avalonia.Controls.Panels;

/// <summary>
/// A vertical stack whose children may stand in another order than they are declared in: each child goes by its
/// <see cref="OrderProperty"/>, the ones of the same order as they are declared. A style can so move a part of
/// a message, as the pictures of a bubble go above its text, without a second copy of it. Hidden children take
/// no room and no spacing, as in a <see cref="StackPanel"/>.
/// </summary>
public class OrderedStackPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        StackPanel.SpacingProperty.AddOwner<OrderedStackPanel>();

    public static readonly AttachedProperty<int> OrderProperty =
        AvaloniaProperty.RegisterAttached<OrderedStackPanel, Control, int>("Order");

    static OrderedStackPanel()
    {
        AffectsMeasure<OrderedStackPanel>(SpacingProperty);
        AffectsParentMeasure<OrderedStackPanel>(OrderProperty);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public static int GetOrder(Control control) => control.GetValue(OrderProperty);
    public static void SetOrder(Control control, int value) => control.SetValue(OrderProperty, value);

    // OrderBy is stable, so children of the same order keep the order they are declared in
    private IEnumerable<Control> Ordered() => Children.Where(child => child.IsVisible).OrderBy(GetOrder);

    protected override Size MeasureOverride(Size availableSize)
    {
        var childAvailable = availableSize.WithHeight(double.PositiveInfinity);
        double width = 0, height = 0;
        int count = 0;
        foreach (var child in Children)
        {
            child.Measure(childAvailable);
            if (!child.IsVisible)
                continue;
            width = System.Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height;
            count++;
        }
        if (count > 1)
            height += Spacing * (count - 1);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        bool first = true;
        foreach (var child in Ordered())
        {
            if (!first)
                y += Spacing;
            first = false;
            var height = child.DesiredSize.Height;
            child.Arrange(new Rect(0, y, finalSize.Width, height));
            y += height;
        }
        return finalSize;
    }
}
