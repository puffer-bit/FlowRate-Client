using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Android.Content;
using Client.Application.Ports;
using Client.Application.Ports.Repositories;
using Client.Application.Push;
using Client.Avalonia.Localization;
using Client.Api;
using Client.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Droid.Platform.Notifications;

/// <summary>
/// Turns a push message into a notification. The system may start the process for the push alone,
/// with no interface, so this reads the local storage through a container of its own.
/// </summary>
internal static class PushMessageHandler
{
    private const string SupportedVersion = "1";

    /// <summary>
    /// The local storage and what a notification does without the interface: replies go over HTTP.
    /// </summary>
    public static readonly Lazy<ServiceProvider> Services = new(() => new ServiceCollection()
        // The media cache, where the avatars are, asks for a logger
        .AddLogging()
        .AddInfrastructure(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
        .AddApi()
        .AddTransient<PushReplies>()
        .BuildServiceProvider());

    /// <summary>
    /// The chat on screen, set by the interface while the application is in front: its messages
    /// are seen as they come and need no notification.
    /// </summary>
    public static (string CoordinatorInstanceId, int ChatId)? ChatOnScreen { get; set; }

    public static async Task HandleAsync(Context context, IDictionary<string, string> data)
    {
        if (!data.TryGetValue("v", out var version) || version != SupportedVersion
            || !data.TryGetValue("k", out var keyId) || !data.TryGetValue("c", out var content))
            return;

        // Unknown key: the push is of a server the user left, or not for this installation at all
        var key = await Services.Value.GetRequiredService<IPushKeysRepository>().GetByKeyIdAsync(keyId);
        if (key == null)
            return;

        var payload = PushCipher.Decrypt(key.Key, content);

        // A push opened by the key of one coordinator must not speak for another
        if (payload == null || payload.CoordinatorId != key.CoordinatorInstanceId)
            return;

        if (ChatOnScreen == (payload.CoordinatorId, payload.ChatId))
            return;

        // A mention gets through a muted chat
        var mutedChats = await Services.Value.GetRequiredService<IMutedChatsRepository>().GetAsync(payload.CoordinatorId);
        if (mutedChats.Contains(payload.ChatId) && !payload.Mention)
            return;

        // The interface may not have run in this process
        Strings.SetLanguage(CultureInfo.CurrentUICulture.Name);
        ChatNotifications.Show(context, payload, Services.Value.GetService<IMediaCache>());
    }
}
