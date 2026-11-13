using System.Threading.Tasks;
using Android.App;
using Android.Content;

namespace Client.Droid.Platform.Notifications;

/// <summary>
/// The "Decline" button of an incoming call notification. It declines over HTTP, without opening FlowRate.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
internal sealed class IncomingCallDeclineReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || IncomingCall.ReadFrom(intent) is not { } call)
            return;

        // Keeps the process alive while the request goes; the system allows about ten seconds
        var pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                await IncomingCallNotifications.DeclineAsync(context, call);
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}
