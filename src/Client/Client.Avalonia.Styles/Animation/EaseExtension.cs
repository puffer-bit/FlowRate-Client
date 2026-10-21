using Avalonia.Animation.Easings;

namespace Client.Avalonia.Styles.Animation;

/// <summary>
/// Markup extension: <c>Easing="{anim:Ease Enter}"</c> gives a transition or an animation one of
/// the curves of <see cref="Motion"/> instead of a curve of its own.
/// </summary>
public class EaseExtension
{
    public EaseExtension()
    {
    }

    public EaseExtension(MotionEase ease)
    {
        Ease = ease;
    }

    public MotionEase Ease { get; set; } = MotionEase.Standard;

    public Easing ProvideValue(IServiceProvider serviceProvider) => Motion.Ease(Ease);
}
