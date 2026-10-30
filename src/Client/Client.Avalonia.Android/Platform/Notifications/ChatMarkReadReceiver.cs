using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Client.Application.Push;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Client.Droid.Platform.Notifications;

/// <summary>
/// The "Mark as read" button of a chat notification: moves the read position of the user on the
/// coordinator over HTTP, like <see cref="ChatReplyReceiver"/>, so the other devices of the user
/// clear their counters too.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
internal sealed class ChatMarkReadReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || ChatNotifications.ReadMarkRead(intent) is not { } read)
            return;

        // The user is done with the messages either way; a position that did not reach the server
        // is moved the next time the chat is opened
        ChatNotifications.Clear(context, read.CoordinatorInstanceId, read.ChatId);

        // Keeps the process alive while the request goes; the system allows about ten seconds
        var pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                await PushMessageHandler.Services.Value.GetRequiredService<PushReplies>()
                    .MarkReadAsync(read.CoordinatorInstanceId, read.ChatId, read.MessageId);
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
