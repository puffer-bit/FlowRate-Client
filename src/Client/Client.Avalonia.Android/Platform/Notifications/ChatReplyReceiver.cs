using System;
using System.Globalization;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Client.Application.Push;
using Client.Avalonia.Localization;
using Client.Core.Push;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Droid.Platform.Notifications;

/// <summary>
/// Takes the reply the user typed into a chat notification and sends it straight to the coordinator
/// over HTTP. The connection of the application is not needed, so it works also in a process the
/// system started for the push alone, and does not bring back the user the server counts as away.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
internal sealed class ChatReplyReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || ChatNotifications.ReadReply(intent) is not { } reply)
            return;

        // Keeps the process alive while the message goes; the system allows about ten seconds
        var pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await PushMessageHandler.Services.Value.GetRequiredService<PushReplies>()
                    .SendAsync(reply.CoordinatorInstanceId, reply.ChatId, reply.Text);

                // A sent reply answers the messages, so their notification goes, which also stops its spinner
                if (result == PushReplyResult.Sent)
                {
                    ChatNotifications.Clear(context, reply.CoordinatorInstanceId, reply.ChatId);
                }
                else
                {
                    // The interface may not have run in this process
                    Strings.SetLanguage(CultureInfo.CurrentUICulture.Name);
                    ChatNotifications.ShowReplyFailed(context, reply.CoordinatorInstanceId, reply.ChatId);
                }
            }
            catch (Exception)
            {
                ChatNotifications.ShowReplyFailed(context, reply.CoordinatorInstanceId, reply.ChatId);
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}
