using Avalonia;
using Avalonia.Data;

namespace Client.Avalonia.Styles.Animation;

/// <summary>
/// Markup extension: <c>Duration="{anim:Motion Short}"</c> binds the duration of a transition or of
/// an animation to <see cref="Motion"/>, so it follows the animation settings while the view is open.
/// </summary>
public class MotionExtension
{
    public MotionExtension()
    {
    }

    public MotionExtension(MotionDuration duration)
    {
        Duration = duration;
    }

    public MotionDuration Duration { get; set; } = MotionDuration.Short;

    public BindingBase ProvideValue(IServiceProvider serviceProvider) => Motion.Observe(Duration).ToBinding();
}
