using Android.Content;

namespace Client.Droid.Platform.Calls;

/// <summary>
/// The buttons of the notification of a voice session. The session lives in this process, which the
/// service keeps alive, so the tap is handed to it directly.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
internal sealed class CallActionReceiver : BroadcastReceiver
{
    // Called on the main thread
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (CallNotification.ReadAction(intent) is { } action)
            CallService.RaiseAction(action);
    }
}
