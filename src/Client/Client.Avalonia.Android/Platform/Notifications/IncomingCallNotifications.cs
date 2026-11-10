using System;
using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Client.Avalonia.Localization;
using Client.Core.Push;

namespace Client.Droid.Platform.Notifications;

internal static class IncomingCallNotifications
{
    internal const string CallExtra = "com.flowrate.client.push.call";
    internal const string ActionExtra = "com.flowrate.client.push.call_action";
    private const string ActionTokenExtra = "com.flowrate.client.push.call_token";
    internal const string RecipientExtra = "com.flowrate.client.push.call_recipient";
    private const string ChannelId = "incoming_calls";
    private static readonly object Sync = new();

    public static void Handle(Context context, PushPayload payload)
    {
        if (payload.CallId is not { } callId || callId == Guid.Empty || payload.CallState is not (0 or 1 or 2)
            || payload.RecipientId is not > 0 || payload.ExpiresAt is not { } expires) return;
        lock (Sync)
        {
            var tag = $"call:{payload.CoordinatorId}";
            var manager = NotificationManagerCompat.From(context);
            // Persist the latest transition so reordered pushes cannot resurrect a dismissed invitation.
            var state = context.GetSharedPreferences("incoming_calls", FileCreationMode.Private)!;
            var lastTime = state.GetLong(tag + ":time", 0);
            var lastId = state.GetString(tag + ":id", null);
            var closed = state.GetBoolean(tag + ":closed", false);
            if (payload.SentTime < lastTime || (lastId == callId.ToString() && closed && payload.CallState == 0)) return;
            var actionToken = lastId == callId.ToString() ? state.GetString(tag + ":token", null) : null;
            actionToken ??= Guid.NewGuid().ToString("N");
            var ended = payload.CallState != 0 || expires <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using (var editor = state.Edit()!)
                editor.PutLong(tag + ":time", payload.SentTime)!
                    .PutString(tag + ":id", callId.ToString())!
                    .PutBoolean(tag + ":closed", ended)!
                    .PutString(tag + ":token", actionToken)!.Commit();
            if (ended)
            {
                manager?.Cancel(tag, 1);
                return;
            }
            if (manager == null || !manager.AreNotificationsEnabled()) return;
            // A live connection already shows the invitation inside FlowRate while it is in front.
            if (PushMessageHandler.IsCallAccountOnScreen?.Invoke(payload.CoordinatorId, payload.RecipientId.Value) == true) return;
            if (OperatingSystem.IsAndroidVersionAtLeast(26)
                && context.GetSystemService(Context.NotificationService) is NotificationManager system)
            {
                var channel = new NotificationChannel(ChannelId, Strings.Get("calls.incoming"), NotificationImportance.High);
                channel.EnableVibration(true);
                system.CreateNotificationChannel(channel);
            }
            var builder = new NotificationCompat.Builder(context, ChannelId)
                .SetSmallIcon(Resource.Drawable.ic_call)!
                .SetContentTitle(Strings.Get("calls.incoming"))!
                .SetContentText(payload.SenderName)!
                .SetCategory(NotificationCompat.CategoryCall)!
                .SetPriority(NotificationCompat.PriorityMax)!
                .SetOngoing(true)!
                .SetOnlyAlertOnce(true)!
                .SetDefaults(NotificationCompat.DefaultAll)!
                .SetTimeoutAfter(Math.Clamp(expires - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 1, 45000))!
                .SetContentIntent(Open(context, payload, null, actionToken))!
                .AddAction(Resource.Drawable.ic_notification, Strings.Get("calls.accept"), Open(context, payload, "accept", actionToken))!
                .AddAction(Resource.Drawable.ic_notification, Strings.Get("calls.decline"), Open(context, payload, "decline", actionToken))!;
            var notification = builder.Build()!;
            notification.Flags |= NotificationFlags.Insistent;
            manager?.Notify(tag, 1, notification);
            if (!OperatingSystem.IsAndroidVersionAtLeast(26)
                && context.GetSystemService(Context.AlarmService) is AlarmManager alarms)
            {
                var intent = new Intent(context, typeof(IncomingCallExpiryReceiver));
                intent.SetData(global::Android.Net.Uri.Parse($"flowrate://calls/{callId}/expire"));
                intent.PutExtra(ChatNotifications.CoordinatorExtra, payload.CoordinatorId);
                intent.PutExtra(CallExtra, callId.ToString());
                var pending = PendingIntent.GetBroadcast(context, 0, intent,
                    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
                // Older systems do not implement notification timeouts.
                if (pending != null) alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, expires, pending);
            }
        }
    }

    private static PendingIntent? Open(Context context, PushPayload payload, string? action, string actionToken)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop | ActivityFlags.NewTask);
        // Intent identity includes the call and action; extras alone do not distinguish pending intents.
        intent.SetData(global::Android.Net.Uri.Parse($"flowrate://calls/{payload.CallId}/{action ?? "open"}"));
        intent.PutExtra(ChatNotifications.CoordinatorExtra, payload.CoordinatorId);
        intent.PutExtra(ChatNotifications.ChatExtra, payload.ChatId);
        intent.PutExtra(CallExtra, payload.CallId.ToString());
        intent.PutExtra(ActionExtra, action);
        intent.PutExtra(ActionTokenExtra, actionToken);
        intent.PutExtra(RecipientExtra, payload.RecipientId!.Value);
        return PendingIntent.GetActivity(context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }

    public static (Guid CallId, int UserId, string Action)? ReadAction(Context context, Intent? intent)
    {
        if (ChatNotifications.ReadTarget(intent) is not { } chat
            || !Guid.TryParse(intent?.GetStringExtra(CallExtra), out var callId)
            || intent?.GetStringExtra(ActionExtra) is not ("accept" or "decline")
            || intent.GetStringExtra(ActionTokenExtra) is not { } token) return null;
        // The launcher is exported; only an action from our immutable pending intent may answer a call.
        var state = context.GetSharedPreferences("incoming_calls", FileCreationMode.Private)!;
        var tag = $"call:{chat.CoordinatorInstanceId}";
        if (state.GetString(tag + ":id", null) != callId.ToString()
            || state.GetString(tag + ":token", null) != token || state.GetBoolean(tag + ":closed", false)) return null;
        return (callId, intent.GetIntExtra(RecipientExtra, 0), intent.GetStringExtra(ActionExtra)!);
    }

    public static void Expire(Context context, string coordinatorId, string callId)
    {
        lock (Sync)
        {
            var tag = $"call:{coordinatorId}";
            var state = context.GetSharedPreferences("incoming_calls", FileCreationMode.Private)!;
            if (state.GetString(tag + ":id", null) != callId) return;
            Clear(context, coordinatorId);
        }
    }

    public static void Clear(Context context, string coordinatorId)
    {
        lock (Sync)
            NotificationManagerCompat.From(context)?.Cancel($"call:{coordinatorId}", 1);
    }
}
