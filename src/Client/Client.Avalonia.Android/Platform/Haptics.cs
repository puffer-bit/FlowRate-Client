using Android.Views;

namespace Client.Droid.Platform;

/// <summary>The short taps of the vibration motor that confirm a gesture.</summary>
internal static class Haptics
{
    /// <summary>A gesture has gone far enough to act, such as a message swiped for a reply.</summary>
    public static void Tick()
    {
        if (MainActivity.Current is not { IsFinishing: false, IsDestroyed: false } activity)
            return;

        // Follows the setting of the system: nothing when touch feedback is turned off
        activity.Window?.DecorView?.PerformHapticFeedback(FeedbackConstants.VirtualKey);
    }
}
