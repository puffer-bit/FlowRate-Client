using Avalonia.Media;

namespace Client.Avalonia.Styles.Scaling;

/// <summary>
/// The size every window draws its content at. The window theme lays the content out under
/// <see cref="Transform"/>, layers of the window included, so dialogs shown over a window follow it too.
/// Popups are windows of their own and take the scale from the control they open at, see PopupStyle.axaml.
/// </summary>
public static class WindowScale
{
    /// <summary>
    /// Shared by all windows: a change of it lays each of them out again.
    /// </summary>
    public static ScaleTransform Transform { get; } = new();

    public static double Factor => Transform.ScaleX;

    /// <summary>
    /// Raised on the thread of <see cref="Apply"/> after the scale has changed.
    /// </summary>
    public static event Action? Changed;

    /// <param name="factor">One is the usual size. Must be called on the UI thread.</param>
    public static void Apply(double factor)
    {
        if (Transform.ScaleX == factor)
            return;

        Transform.ScaleX = factor;
        Transform.ScaleY = factor;
        Changed?.Invoke();
    }
}
