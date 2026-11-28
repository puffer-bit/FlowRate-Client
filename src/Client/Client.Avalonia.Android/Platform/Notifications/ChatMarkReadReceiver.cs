using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Client.Application.Push;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Client.Droid.Platform.Notifications;

/// <summary>
/// The "Mark as read" button stores the read position on this device.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
internal sealed class ChatMarkReadReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || ChatNotifications.ReadMarkRead(intent) is not { } read)
            return;

        // Dismiss the notification immediately; persist the position below.
        ChatNotifications.Clear(context, read.CoordinatorInstanceId, read.ChatId);

        // Keeps the process alive while the local position is saved.
        var pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await PushMessageHandler.Services.Value.GetRequiredService<PushReplies>()
                    .MarkReadAsync(read.CoordinatorInstanceId, read.ChatId, read.MessageId);
                if (result == Client.Core.Push.PushReplyResult.Sent)
                    PushMessageHandler.ChatMarkedRead?.Invoke(read.CoordinatorInstanceId, read.ChatId, read.MessageId);
            }
            catch (Exception exception)
            {
                // Nothing to show: the notification is gone already
                Log.ForContext<ChatMarkReadReceiver>().Warning(exception,
                    "Failed to mark chat {ChatId} read from a notification", read.ChatId);
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}
