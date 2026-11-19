using System;
using System.Collections.Generic;
using Android.App;
using Client.Droid.Platform.Notifications;
using Firebase.Messaging;
using Serilog;

namespace Client.Droid.Platform.Firebase;

[Service(Name = "Client.Droid.Platform.Firebase.ApplicationFirebaseMessagingService", Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public class ApplicationFirebaseMessagingService : FirebaseMessagingService
{
    // The binding marks the whole callback obsolete; Firebase itself does not
#pragma warning disable CS0672, CS0618
    public override void OnNewToken(string token)
    {
        base.OnNewToken(token);

        FirebasePushTokenProvider.ReportNewToken(token);
    }
#pragma warning restore CS0672, CS0618

    // Called on a worker thread of Firebase, which gives the service about ten seconds
    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);

        try
        {
            var data = new Dictionary<string, string>(message.Data);
            PushMessageHandler.HandleAsync(this, data).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Log.ForContext<ApplicationFirebaseMessagingService>().Error(exception, "Failed to show a push notification");
        }
    }
}
