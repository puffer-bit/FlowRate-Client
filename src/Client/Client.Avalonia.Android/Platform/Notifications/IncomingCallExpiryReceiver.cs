using Android.App;
using Android.Content;

namespace Client.Droid.Platform.Notifications;

[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class IncomingCallExpiryReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context != null && intent?.GetStringExtra(ChatNotifications.CoordinatorExtra) is { } coordinatorId
            && intent.GetStringExtra(IncomingCallNotifications.CallExtra) is { } callId)
            IncomingCallNotifications.Expire(context, coordinatorId, callId);
    }
}
