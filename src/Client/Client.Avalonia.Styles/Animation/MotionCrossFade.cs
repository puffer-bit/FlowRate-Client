using Avalonia;
using Avalonia.Animation;

namespace Client.Avalonia.Styles.Animation;

/// <summary>
/// A cross-fade between pages whose length is one of <see cref="Motion"/>. The duration of the stock
/// <see cref="CrossFade"/> is not a property that can be bound, so it is set before every run.
/// </summary>
public class MotionCrossFade : IPageTransition
{
    private readonly CrossFade _fade = new()
    {
        FadeInEasing = Motion.Ease(MotionEase.Enter),
        FadeOutEasing = Motion.Ease(MotionEase.Exit)
    };

    public MotionDuration Duration { get; set; } = MotionDuration.Short;

    public Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        _fade.Duration = Motion.Get(Duration);
        return ((IPageTransition)_fade).Start(from, to, forward, cancellationToken);
    }
}
