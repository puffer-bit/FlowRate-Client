using System;
using Android.App;
using Android.Content;
using Android.Graphics;
using AndroidX.Core.App;
using AndroidX.Core.Graphics.Drawable;
using Client.Application.Ports;
using Client.Avalonia.Localization;
using Client.Core.Push;
using Client.Core.Users;
using Person = AndroidX.Core.App.Person;
using RemoteInput = AndroidX.Core.App.RemoteInput;

namespace Client.Droid.Platform.Notifications;

/// <summary>
/// The system notifications about chat messages: one per chat, which collects the messages that
/// came while the chat was not looked at.
/// </summary>
internal static class ChatNotifications
{
    public const string CoordinatorExtra = "com.flowrate.client.push.coordinator";
    public const string ChatExtra = "com.flowrate.client.push.chat";
    public const string MessageExtra = "com.flowrate.client.push.message";

    private const string ChannelId = "messages";

    private const string ReplyTextKey = "com.flowrate.client.push.reply_text";

    // Mentions have a channel of their own, so the user can let them sound while messages are quiet
    private const string MentionsChannelId = "mentions";

    // The channel of the first test builds; a channel cannot be changed, only replaced
    private const string LegacyChannelId = "high_importance_channel";

    public static void EnsureChannel(Context context)
    {
        // Older systems have no channels
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        if (context.GetSystemService(Context.NotificationService) is not NotificationManager manager)
            return;

        manager.DeleteNotificationChannel(LegacyChannelId);

        // Created again on every start, which only renames it: the system keeps what the user set for it
        var channel = new NotificationChannel(ChannelId, Strings.Get("push.channel.messages"), NotificationImportance.High);
        channel.EnableVibration(true);
        manager.CreateNotificationChannel(channel);

        var mentions = new NotificationChannel(MentionsChannelId, Strings.Get("push.channel.mentions"), NotificationImportance.High);
        mentions.EnableVibration(true);
        manager.CreateNotificationChannel(mentions);
    }

    /// <param name="mediaCache">Where the avatar of the sender is looked for; without it there is none.</param>
    public static void Show(Context context, PushPayload payload, IMediaCache? mediaCache = null)
    {
        EnsureChannel(context);

        var manager = NotificationManagerCompat.From(context);
        if (manager == null || !manager.AreNotificationsEnabled())
            return;

        var notificationId = GetId(payload.CoordinatorId, payload.ChatId);
        var sentTime = payload.SentTime > 0 ? payload.SentTime : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var style = FindShownStyle(context, notificationId)
                    ?? new NotificationCompat.MessagingStyle(CreatePerson(payload.ChatName));
        style.SetConversationTitle(payload.ChatName);
        style.SetGroupConversation(true);
        style.AddMessage(GetText(payload), sentTime, CreatePerson(payload.SenderName, FindAvatar(payload, mediaCache)));

        Notify(context, payload.CoordinatorId, payload.ChatId, payload.MessageId, style,
            payload.Mention ? MentionsChannelId : ChannelId, sentTime, isSilent: false);
    }

    /// <summary>
    /// Puts the reply that did not go back into the notification of its chat, so that the user knows
    /// to send it again from the application.
    /// </summary>
    public static void ShowReplyFailed(Context context, string coordinatorInstanceId, int chatId)
    {
        var manager = NotificationManagerCompat.From(context);
        var notificationId = GetId(coordinatorInstanceId, chatId);
        if (manager == null || !manager.AreNotificationsEnabled()
            || FindShown(context, notificationId) is not { } shown
            || NotificationCompat.MessagingStyle.ExtractMessagingStyleFromNotification(shown) is not { } style)
            return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // A message with no person is shown as a note of the system
        style.AddMessage(Strings.Get("push.reply.failed"), now, (Person?)null);
        Notify(context, coordinatorInstanceId, chatId, shown.Extras?.GetInt(MessageExtra) ?? 0, style, ChannelId,
            now, isSilent: true);
    }

    /// <returns>The chat and the text of a reply typed into a notification, or null for an intent that is not one.</returns>
    public static (string CoordinatorInstanceId, int ChatId, string Text)? ReadReply(Intent? intent)
    {
        var text = intent == null ? null : RemoteInput.GetResultsFromIntent(intent)?.GetCharSequence(ReplyTextKey)?.ToString();

        return string.IsNullOrWhiteSpace(text) || ReadTarget(intent) is not { } chat
            ? null
            : (chat.CoordinatorInstanceId, chat.ChatId, text);
    }

    /// <returns>The chat and the newest message of a notification whose "Mark as read" was tapped.</returns>
    public static (string CoordinatorInstanceId, int ChatId, int MessageId)? ReadMarkRead(Intent? intent)
    {
        var messageId = intent?.GetIntExtra(MessageExtra, 0) ?? 0;

        return messageId <= 0 || ReadTarget(intent) is not { } chat
            ? null
            : (chat.CoordinatorInstanceId, chat.ChatId, messageId);
    }

    /// <param name="messageId">The newest message shown, which "Mark as read" reads up to; 0 for none.</param>
    private static void Notify(Context context, string coordinatorInstanceId, int chatId, int messageId,
        NotificationCompat.MessagingStyle style, string channelId, long when, bool isSilent)
    {
        var notificationId = GetId(coordinatorInstanceId, chatId);

        var builder = new NotificationCompat.Builder(context, channelId);
        builder.SetSmallIcon(Resource.Drawable.ic_notification);
        builder.SetStyle(style);
        builder.SetCategory(NotificationCompat.CategoryMessage);
        builder.SetPriority(NotificationCompat.PriorityHigh);
        builder.SetWhen(when);
        builder.SetShowWhen(true);
        builder.SetAutoCancel(true);
        builder.SetOnlyAlertOnce(isSilent);
        builder.SetContentIntent(CreateOpenIntent(context, coordinatorInstanceId, chatId, notificationId));
        builder.AddAction(CreateReplyAction(context, coordinatorInstanceId, chatId, notificationId));
        if (messageId > 0)
        {
            builder.AddAction(CreateMarkReadAction(context, coordinatorInstanceId, chatId, messageId, notificationId));

            // Kept with the notification, so that a later note of the system keeps the button
            var extras = new Android.OS.Bundle();
            extras.PutInt(MessageExtra, messageId);
            builder.AddExtras(extras);
        }

        NotificationManagerCompat.From(context)?.Notify(notificationId, builder.Build());
    }

    private static NotificationCompat.Action CreateReplyAction(Context context, string coordinatorInstanceId,
        int chatId, int notificationId)
    {
        var intent = new Intent(context, typeof(ChatReplyReceiver));
        intent.PutExtra(CoordinatorExtra, coordinatorInstanceId);
        intent.PutExtra(ChatExtra, chatId);

        // Mutable, or the system cannot put the typed text into the intent
        var pendingIntent = PendingIntent.GetBroadcast(context, notificationId, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable);

        var remoteInput = new RemoteInput.Builder(ReplyTextKey)
            .SetLabel(Strings.Get("push.reply.hint"))
            .Build();

        return new NotificationCompat.Action.Builder(Resource.Drawable.ic_notification,
                Strings.Get("push.reply"), pendingIntent)
            .AddRemoteInput(remoteInput)
            .SetSemanticAction(NotificationCompat.Action.SemanticActionReply)
            .SetShowsUserInterface(false)
            .Build()!;
    }

    private static NotificationCompat.Action CreateMarkReadAction(Context context, string coordinatorInstanceId,
        int chatId, int messageId, int notificationId)
    {
        var intent = new Intent(context, typeof(ChatMarkReadReceiver));
        intent.PutExtra(CoordinatorExtra, coordinatorInstanceId);
        intent.PutExtra(ChatExtra, chatId);
        intent.PutExtra(MessageExtra, messageId);

        var pendingIntent = PendingIntent.GetBroadcast(context, notificationId, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        return new NotificationCompat.Action.Builder(Resource.Drawable.ic_notification,
                Strings.Get("push.markRead"), pendingIntent)
            .SetSemanticAction(NotificationCompat.Action.SemanticActionMarkAsRead)
            .SetShowsUserInterface(false)
            .Build()!;
    }

    /// <summary>
    /// Takes away the notification of a chat the user is looking at.
    /// </summary>
    public static void Clear(Context context, string coordinatorInstanceId, int chatId) =>
        NotificationManagerCompat.From(context)?.Cancel(GetId(coordinatorInstanceId, chatId));

    /// <returns>The chat a tapped notification leads to, or null for an intent that is not ours.</returns>
    public static (string CoordinatorInstanceId, int ChatId)? ReadTarget(Intent? intent)
    {
        var coordinatorInstanceId = intent?.GetStringExtra(CoordinatorExtra);
        var chatId = intent?.GetIntExtra(ChatExtra, 0) ?? 0;

        return coordinatorInstanceId == null || chatId <= 0 ? null : (coordinatorInstanceId, chatId);
    }

    private static Person CreatePerson(string name, IconCompat? avatar = null)
    {
        var builder = new Person.Builder();
        builder.SetName(name);
        if (avatar != null)
            builder.SetIcon(avatar);
        return builder.Build()!;
    }

    // Only an avatar that is kept already: a push comes with no connection to fetch one over
    private static IconCompat? FindAvatar(PushPayload payload, IMediaCache? mediaCache)
    {
        if (mediaCache == null || AvatarAddress.IdOf(payload.SenderAvatarUrl) is not { } avatarId)
            return null;

        try
        {
            if (mediaCache.FindAvatar(payload.CoordinatorId, avatarId) is not { } file)
                return null;

            using var picture = BitmapFactory.DecodeFile(file);

            return picture == null ? null : IconCompat.CreateWithBitmap(CropCircle(picture));
        }
        catch (Exception)
        {
            // A notification without a picture is better than no notification
            return null;
        }
    }

    // The system shows the icon of a person as it is given, so the round shape is cut here
    private static Bitmap CropCircle(Bitmap picture)
    {
        var size = Math.Min(picture.Width, picture.Height);
        var round = Bitmap.CreateBitmap(size, size, Bitmap.Config.Argb8888!)!;

        using var canvas = new Canvas(round);
        using var paint = new Paint(PaintFlags.AntiAlias);
        using var shader = new BitmapShader(picture, Shader.TileMode.Clamp!, Shader.TileMode.Clamp!);
        paint.SetShader(shader);
        canvas.DrawCircle(size / 2f, size / 2f, size / 2f, paint);

        return round;
    }

    private static string GetText(PushPayload payload)
    {
        if (!string.IsNullOrEmpty(payload.Text))
            return payload.Text;

        return payload.Attachment switch
        {
            PushAttachmentKind.Image => Strings.Get("push.attachment.image"),
            PushAttachmentKind.Video => Strings.Get("push.attachment.video"),
            PushAttachmentKind.File => Strings.Get("push.attachment.file"),
            _ => Strings.Get("chat.attachment")
        };
    }

    // The messages already shown for the chat are read back from the system, so nothing is kept here
    private static NotificationCompat.MessagingStyle? FindShownStyle(Context context, int notificationId) =>
        FindShown(context, notificationId) is { } shown
            ? NotificationCompat.MessagingStyle.ExtractMessagingStyleFromNotification(shown)
            : null;

    private static Notification? FindShown(Context context, int notificationId)
    {
        if (context.GetSystemService(Context.NotificationService) is not NotificationManager manager)
            return null;

        foreach (var shown in manager.GetActiveNotifications() ?? [])
        {
            if (shown.Id == notificationId && shown.Notification != null)
                return shown.Notification;
        }

        return null;
    }

    private static PendingIntent? CreateOpenIntent(Context context, string coordinatorInstanceId, int chatId,
        int notificationId)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop | ActivityFlags.NewTask);
        intent.PutExtra(CoordinatorExtra, coordinatorInstanceId);
        intent.PutExtra(ChatExtra, chatId);

        // The request code tells the intents of different chats apart: their extras alone do not
        return PendingIntent.GetActivity(context, notificationId, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }

    // Stable across runs of the process, unlike string.GetHashCode
    private static int GetId(string coordinatorInstanceId, int chatId)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in coordinatorInstanceId)
                hash = (hash ^ c) * 16777619;

            return (hash * 397) ^ chatId;
        }
    }
}
