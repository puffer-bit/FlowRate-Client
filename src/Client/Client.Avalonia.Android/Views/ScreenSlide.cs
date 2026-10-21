using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Client.Avalonia.Styles.Animation;

namespace Client.Droid.Views;

/// <summary>
/// The transition between screens: the new one pushes the old one out sideways, from the right when
/// going deeper and from the left when going back. It takes one of the durations of
/// <see cref="Motion"/>, so it follows the animation settings as everything else that moves.
/// </summary>
public class ScreenSlide : IPageTransition
{
    public MotionDuration Duration { get; set; } = MotionDuration.Long;

    public Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        var duration = Motion.Get(Duration);

        // With animations switched off the screens are only swapped
        if (duration <= TimeSpan.Zero)
        {
            if (to != null)
                to.IsVisible = true;
            if (from != null)
                from.IsVisible = false;

            return Task.CompletedTask;
        }

        var slide = new PageSlide(duration)
        {
            SlideInEasing = Motion.Ease(MotionEase.Standard),
            SlideOutEasing = Motion.Ease(MotionEase.Standard)
        };

        return slide.Start(from, to, forward, cancellationToken);
    }
}
