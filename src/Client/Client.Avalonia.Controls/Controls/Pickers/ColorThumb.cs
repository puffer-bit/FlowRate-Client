using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Client.Avalonia.Controls.Pickers;

/// <summary>
/// The round handle of <see cref="ColorSquare"/> and <see cref="HueSlider"/>: the color under it
/// inside a ring, with a thin line around the ring so it stays visible on any color.
/// </summary>
internal static class ColorThumb
{
    public const double Radius = 8;

    private const double RingThickness = 2.5;
    private const double OutlineThickness = 1;

    public static void Draw(DrawingContext context, Point center, Color color, IBrush? ring, IBrush? outline)
    {
        context.DrawEllipse(null, new Pen(outline, OutlineThickness), center,
            Radius + OutlineThickness / 2, Radius + OutlineThickness / 2);
        context.DrawEllipse(new ImmutableSolidColorBrush(color), new Pen(ring, RingThickness), center,
            Radius - RingThickness / 2, Radius - RingThickness / 2);
    }
}
