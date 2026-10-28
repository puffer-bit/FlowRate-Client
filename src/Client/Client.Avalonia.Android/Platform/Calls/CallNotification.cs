using System;
using System.Collections.Generic;
using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Client.Avalonia.Localization;

namespace Client.Droid.Platform.Calls;

/// <summary>
/// The ongoing notification of the voice session the user is in, with the buttons of the microphone
/// and of leaving.
/// </summary>
internal static class CallNotification
{
    public const string ActionExtra = "com.flowrate.client.call.action";

    private const string ChannelId = "calls";

    public static void EnsureChannel(Context context)
    {
        // Older systems have no channels
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        if (context.GetSystemService(Context.NotificationService) is not NotificationManager manager)
            return;

        // Low: it is there for the whole session and must neither sound nor pop up
        var channel = new NotificationChannel(ChannelId, Strings.Get("call.channel"), NotificationImportance.Low);
        channel.SetShowBadge(false);
        manager.CreateNotificationChannel(channel);
    }

    public static Notification Build(Context context, CallState state)
    {
        EnsureChannel(context);

        var builder = new NotificationCompat.Builder(context, ChannelId);
        builder.SetSmallIcon(Resource.Drawable.ic_call);
        builder.SetContentTitle(state.SessionName);
        builder.SetContentText(GetText(state));
        builder.SetCategory(NotificationCompat.CategoryCall);
        builder.SetPriority(NotificationCompat.PriorityLow);
        builder.SetOngoing(true);
        builder.SetOnlyAlertOnce(true);
        builder.SetShowWhen(state.Since > 0);
        builder.SetWhen(state.Since);
        builder.SetUsesChronometer(state.Since > 0);
        // Shown at once, not after the ten seconds the system otherwise waits for a short service
        builder.SetForegroundServiceBehavior(NotificationCompat.ForegroundServiceImmediate);
        builder.SetContentIntent(CreateOpenIntent(context));
        builder.AddAction(CreateAction(context, CallAction.ToggleMicrophone,
            Strings.Get(state.IsMicrophoneOn ? "call.microphoneOff" : "call.microphoneOn")));
        builder.AddAction(CreateAction(context, CallAction.Leave, Strings.Get("call.leave")));

        return builder.Build()!;
    }

    /// <returns>The button of an intent of the notification, or null for an intent that is not one.</returns>
    public static CallAction? ReadAction(Intent? intent) =>
        intent?.GetIntExtra(ActionExtra, -1) is var action && Enum.IsDefined(typeof(CallAction), action)
            ? (CallAction)action
            : null;

    private static string GetText(CallState state)
    {
        var parts = new List<string>();
        if (!state.IsMicrophoneOn)
            parts.Add(Strings.Get("call.text.microphoneOff"));
        if (!state.IsSoundOn)
            parts.Add(Strings.Get("call.text.soundOff"));

        return parts.Count == 0 ? Strings.Get("call.text.active") : string.Join(" · ", parts);
    }

    private static NotificationCompat.Action CreateAction(Context context, CallAction action, string title)
    {
        var intent = new Intent(context, typeof(CallActionReceiver));
        intent.PutExtra(ActionExtra, (int)action);

        // The request code tells the buttons apart: their extras alone do not
        var pendingIntent = PendingIntent.GetBroadcast(context, (int)action, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        return new NotificationCompat.Action.Builder(Resource.Drawable.ic_call, title, pendingIntent)
            .SetShowsUserInterface(false)
            .Build()!;
    }

    private static PendingIntent? CreateOpenIntent(Context context)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop | ActivityFlags.NewTask);

        return PendingIntent.GetActivity(context, 0, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }
}
