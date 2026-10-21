using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Client.Avalonia.Styles.Animation;

/// <summary>
/// A brush transition that blends solid colors with premultiplied alpha.
/// The stock BrushTransition blends the color channels and the alpha separately, so going from
/// Transparent (#00FFFFFF, a transparent white) to any color passes through a whitish flash.
/// Here a transparent end contributes no color at all, and the blend goes straight from one color to the other.
/// </summary>
public class SmoothBrushTransition : InterpolatingTransitionBase<IBrush?>
{
    protected override IBrush? Interpolate(double progress, IBrush? from, IBrush? to)
    {
        if (!TryGetColor(from, out var fromColor) || !TryGetColor(to, out var toColor))
        {
            // Gradients and other brushes cannot be blended per channel; switch halfway like the stock transition
            return progress < 0.5 ? from : to;
        }

        return new ImmutableSolidColorBrush(Blend(fromColor, toColor, progress));
    }

    // A missing brush draws nothing, which is the same as a fully transparent color
    private static bool TryGetColor(IBrush? brush, out Color color)
    {
        switch (brush)
        {
            case null:
                color = default;
                return true;
            case ISolidColorBrush solid:
                color = solid.Color;
                // Fold the brush opacity into the color so both ends are compared on the same scale
                color = new Color((byte)Math.Round(color.A * solid.Opacity), color.R, color.G, color.B);
                return true;
            default:
                color = default;
                return false;
        }
    }

    private static Color Blend(Color from, Color to, double progress)
    {
        double fromAlpha = from.A / 255.0;
        double toAlpha = to.A / 255.0;
        double alpha = fromAlpha + (toAlpha - fromAlpha) * progress;

        if (alpha <= 0)
            return Color.FromArgb(0, to.R, to.G, to.B);

        // Each end weighs in by its own alpha, then the result is un-premultiplied
        byte Channel(byte fromChannel, byte toChannel)
        {
            double premultiplied = fromChannel * fromAlpha * (1 - progress) + toChannel * toAlpha * progress;
            return (byte)Math.Clamp(Math.Round(premultiplied / alpha), 0, 255);
        }

        return Color.FromArgb(
            (byte)Math.Round(alpha * 255),
            Channel(from.R, to.R),
            Channel(from.G, to.G),
            Channel(from.B, to.B));
    }
}
