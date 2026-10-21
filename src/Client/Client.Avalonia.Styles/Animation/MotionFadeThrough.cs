using Avalonia;
using Avalonia.Animation;
using Avalonia.Styling;

namespace Client.Avalonia.Styles.Animation;

/// <summary>
/// A transition between pages that fades the old one out before the new one fades in, so the two
/// are never drawn over each other: two pages of text, cross-faded, read as a smear. Each half
/// takes one of <see cref="Motion"/>.
/// </summary>
public class MotionFadeThrough : IPageTransition
{
    public MotionDuration Duration { get; set; } = MotionDuration.Short;

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        var duration = Motion.Get(Duration);

        // The new page is already in the tree and must not show through the old one
        if (to != null)
            to.Opacity = 0;

        try
        {
            if (from != null)
            {
                await Fade(1, 0, duration, MotionEase.Exit).RunAsync(from, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                    return;

                from.IsVisible = false;
            }

            if (to != null)
            {
                to.IsVisible = true;
                await Fade(0, 1, duration, MotionEase.Enter).RunAsync(to, cancellationToken);
            }
        }
        finally
        {
            // Also when cancelled: the next transition starts from this page
            if (to != null)
                to.Opacity = 1;
        }
    }

    private static global::Avalonia.Animation.Animation Fade(double start, double end, TimeSpan duration, MotionEase ease) => new()
    {
        Duration = duration,
        Easing = Motion.Ease(ease),
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, start) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, end) } }
        }
    };
}
