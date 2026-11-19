using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Media;
using AndroidX.Core.App;
using AndroidX.Core.Graphics.Drawable;
using Client.Application.Ports;
using Client.Application.Push;
using Client.Avalonia.Localization;
using Client.Core.Push;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Person = AndroidX.Core.App.Person;

namespace Client.Droid.Platform.Notifications;

/// <summary>
/// A direct call that rings for this device: what the notification and the screen of the call show and
/// answer with.
/// </summary>
/// <param name="ActionToken">Proves that an action comes from our own notification or screen.</param>
internal sealed record IncomingCall(string CoordinatorId, int ChatId, Guid CallId, int RecipientId,
    string CallerName, string? CallerAvatarUrl, long ExpiresAt, string ActionToken)
{
    private const string NameExtra = "com.flowrate.client.push.call_name";
    private const string AvatarExtra = "com.flowrate.client.push.call_avatar";
    private const string ExpiresExtra = "com.flowrate.client.push.call_expires";

    public void WriteTo(Intent intent)
    {
        intent.PutExtra(ChatNotifications.CoordinatorExtra, CoordinatorId);
        intent.PutExtra(ChatNotifications.ChatExtra, ChatId);
        intent.PutExtra(IncomingCallNotifications.CallExtra, CallId.ToString());
        intent.PutExtra(IncomingCallNotifications.RecipientExtra, RecipientId);
        intent.PutExtra(IncomingCallNotifications.ActionTokenExtra, ActionToken);
        intent.PutExtra(NameExtra, CallerName);
        intent.PutExtra(AvatarExtra, CallerAvatarUrl);
        intent.PutExtra(ExpiresExtra, ExpiresAt);
    }

    public static IncomingCall? ReadFrom(Intent? intent)
    {
        if (ChatNotifications.ReadTarget(intent) is not { } chat
            || !Guid.TryParse(intent?.GetStringExtra(IncomingCallNotifications.CallExtra), out var callId)
            || intent.GetStringExtra(IncomingCallNotifications.ActionTokenExtra) is not { } token)
            return null;
        return new IncomingCall(chat.CoordinatorInstanceId, chat.ChatId, callId,
            intent.GetIntExtra(IncomingCallNotifications.RecipientExtra, 0), intent.GetStringExtra(NameExtra) ?? "",
            intent.GetStringExtra(AvatarExtra), intent.GetLongExtra(ExpiresExtra, 0), token);
    }
}

internal static class IncomingCallNotifications
{
    internal const string CallExtra = "com.flowrate.client.push.call";
    internal const string ActionExtra = "com.flowrate.client.push.call_action";
    internal const string ActionTokenExtra = "com.flowrate.client.push.call_token";
    internal const string RecipientExtra = "com.flowrate.client.push.call_recipient";
    // The sound of a channel is fixed once created: the first channel rang with the sound of a message
    private const string ChannelId = "incoming_calls_ringtone";
    private const string LegacyChannelId = "incoming_calls";
    private static readonly long[] VibrationPattern = [0, 1000, 1000];
    private static readonly object Sync = new();

    /// <summary>
    /// Raised, on any thread, with the coordinator and the call when its invitation is gone from this
    /// device: answered, declined, ended or expired. A null call stands for any call of the coordinator.
    /// </summary>
    public static event Action<string, Guid?>? Closed;

    public static void Handle(Context context, PushPayload payload)
    {
        if (payload.CallId is not { } callId || callId == Guid.Empty || payload.CallState is not (0 or 1 or 2)
            || payload.RecipientId is not > 0 || payload.ExpiresAt is not { } expires) return;
        lock (Sync)
        {
            var tag = Tag(payload.CoordinatorId);
            var manager = NotificationManagerCompat.From(context);
            // Persist the latest transition so reordered pushes cannot resurrect a dismissed invitation.
            var state = State(context);
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
                Closed?.Invoke(payload.CoordinatorId, callId);
                return;
            }
            if (manager == null || !manager.AreNotificationsEnabled()) return;
            // A live connection already shows the invitation inside FlowRate while it is in front.
            if (PushMessageHandler.IsCallAccountOnScreen?.Invoke(payload.CoordinatorId, payload.RecipientId.Value) == true) return;

            var call = new IncomingCall(payload.CoordinatorId, payload.ChatId, callId, payload.RecipientId.Value,
                payload.SenderName, payload.SenderAvatarUrl, expires, actionToken);
            manager.Notify(tag, 1, Build(context, call));
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

    private static Notification Build(Context context, IncomingCall call)
    {
        EnsureChannel(context);
        var avatar = ChatNotifications.FindRoundAvatar(call.CoordinatorId, call.CallerAvatarUrl,
            PushMessageHandler.Services.Value.GetService<IMediaCache>());
        var person = new Person.Builder()
            .SetName(string.IsNullOrEmpty(call.CallerName) ? Strings.Get("calls.incoming") : call.CallerName)!
            .SetImportant(true)!;
        if (avatar != null)
            person.SetIcon(IconCompat.CreateWithBitmap(avatar));
        var caller = person.Build()!;
        var screen = ShowScreen(context, call);
        var builder = new NotificationCompat.Builder(context, ChannelId)
            .SetSmallIcon(Resource.Drawable.ic_stat_flowrate)!
            .SetContentTitle(call.CallerName)!
            .SetContentText(Strings.Get("calls.incoming"))!
            .SetCategory(NotificationCompat.CategoryCall)!
            .SetPriority(NotificationCompat.PriorityMax)!
            .SetVisibility(NotificationCompat.VisibilityPublic)!
            .SetOngoing(true)!
            .SetAutoCancel(false)!
            .SetOnlyAlertOnce(true)!
            .SetVibrate(VibrationPattern)!
            .SetTimeoutAfter(Math.Clamp(call.ExpiresAt - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 1, 45000))!
            .SetContentIntent(screen)!
            // The screen of the call, over the lock screen and with the display woken, as a phone call has
            .SetFullScreenIntent(screen, true)!
            .AddPerson(caller)!
            .SetStyle(NotificationCompat.CallStyle.ForIncomingCall(caller, Decline(context, call)!,
                Accept(context, call)!))!;
        if (avatar != null)
            builder.SetLargeIcon(avatar);
        if (RingtoneManager.GetDefaultUri(RingtoneType.Ringtone) is { } ringtone)
            builder.SetSound(ringtone, (int)global::Android.Media.Stream.Ring);
        var notification = builder.Build()!;
        // Repeats the ringtone and the vibration until the call is answered, declined or gone
        notification.Flags |= NotificationFlags.Insistent;
        return notification;
    }

    private static void EnsureChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)
            || context.GetSystemService(Context.NotificationService) is not NotificationManager system) return;
        system.DeleteNotificationChannel(LegacyChannelId);
        if (system.GetNotificationChannel(ChannelId) != null) return;
        var channel = new NotificationChannel(ChannelId, Strings.Get("calls.incoming"), NotificationImportance.High);
        channel.LockscreenVisibility = NotificationVisibility.Public;
        channel.EnableVibration(true);
        channel.SetVibrationPattern(VibrationPattern);
        channel.SetSound(RingtoneManager.GetDefaultUri(RingtoneType.Ringtone), new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.NotificationRingtone)!
            .SetContentType(AudioContentType.Sonification)!
            .Build());
        system.CreateNotificationChannel(channel);
    }

    private static PendingIntent? ShowScreen(Context context, IncomingCall call)
    {
        var intent = new Intent(context, typeof(IncomingCallActivity));
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.NoUserAction);
        // Intent identity includes the call; extras alone do not distinguish pending intents.
        intent.SetData(global::Android.Net.Uri.Parse($"flowrate://calls/{call.CallId}/screen"));
        call.WriteTo(intent);
        return PendingIntent.GetActivity(context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }

    private static PendingIntent? Accept(Context context, IncomingCall call) =>
        PendingIntent.GetActivity(context, 0, CreateAcceptIntent(context, call),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

    private static PendingIntent? Decline(Context context, IncomingCall call)
    {
        var intent = new Intent(context, typeof(IncomingCallDeclineReceiver));
        intent.SetData(global::Android.Net.Uri.Parse($"flowrate://calls/{call.CallId}/decline"));
        call.WriteTo(intent);
        return PendingIntent.GetBroadcast(context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }

    /// <summary>
    /// Opens FlowRate to answer the call: the application connects and joins it.
    /// </summary>
    public static Intent CreateAcceptIntent(Context context, IncomingCall call)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop | ActivityFlags.NewTask);
        intent.SetData(global::Android.Net.Uri.Parse($"flowrate://calls/{call.CallId}/accept"));
        call.WriteTo(intent);
        intent.PutExtra(ActionExtra, "accept");
        return intent;
    }

    public static (Guid CallId, int UserId, string Action)? ReadAction(Context context, Intent? intent)
    {
        if (IncomingCall.ReadFrom(intent) is not { } call
            || intent?.GetStringExtra(ActionExtra) is not ("accept" or "decline")) return null;
        // The launcher is exported; only an action from our immutable pending intent may answer a call.
        if (!IsPending(context, call)) return null;
        return (call.CallId, call.RecipientId, intent.GetStringExtra(ActionExtra)!);
    }

    /// <summary>
    /// Whether the invitation is still the latest of its coordinator, not closed, and the action proves to be ours.
    /// </summary>
    public static bool IsPending(Context context, IncomingCall call)
    {
        lock (Sync)
        {
            var state = State(context);
            var tag = Tag(call.CoordinatorId);
            return state.GetString(tag + ":id", null) == call.CallId.ToString()
                   && state.GetString(tag + ":token", null) == call.ActionToken
                   && !state.GetBoolean(tag + ":closed", false);
        }
    }

    /// <summary>
    /// The "Decline" button: the invitation goes from this device at once, and the server is told over HTTP,
    /// so that the call stops ringing for the caller without FlowRate having to start and connect.
    /// </summary>
    public static async Task DeclineAsync(Context context, IncomingCall call)
    {
        lock (Sync)
        {
            if (!IsPending(context, call)) return;
            using (var editor = State(context).Edit()!)
                editor.PutBoolean(Tag(call.CoordinatorId) + ":closed", true)!.Commit();
            NotificationManagerCompat.From(context)?.Cancel(Tag(call.CoordinatorId), 1);
        }
        Closed?.Invoke(call.CoordinatorId, call.CallId);

        try
        {
            var result = await PushMessageHandler.Services.Value.GetRequiredService<PushReplies>()
                .DeclineCallAsync(call.CoordinatorId, call.CallId);
            // Not declined, the call still rings out for the caller when it expires
            if (result != PushReplyResult.Sent)
                Log.ForContext(typeof(IncomingCallNotifications))
                    .Warning("Declining call {CallId} from a notification failed: {Result}", call.CallId, result);
        }
        catch (Exception exception)
        {
            Log.ForContext(typeof(IncomingCallNotifications))
                .Warning(exception, "Declining call {CallId} from a notification failed", call.CallId);
        }
    }

    public static void Expire(Context context, string coordinatorId, string callId)
    {
        lock (Sync)
        {
            var state = State(context);
            if (state.GetString(Tag(coordinatorId) + ":id", null) != callId) return;
            Clear(context, coordinatorId);
        }
    }

    public static void Clear(Context context, string coordinatorId)
    {
        lock (Sync)
            NotificationManagerCompat.From(context)?.Cancel(Tag(coordinatorId), 1);
        Closed?.Invoke(coordinatorId, null);
    }

    private static string Tag(string coordinatorId) => $"call:{coordinatorId}";

    private static ISharedPreferences State(Context context) =>
        context.GetSharedPreferences("incoming_calls", FileCreationMode.Private)!;
}
