using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Client.Application.Coordinator;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using Client.Core.Push;
using Client.Core.Users;
using Client.ViewModels.Sessions.List;
using Microsoft.Extensions.Logging;

namespace Client.ViewModels.Main;

/// <summary>
/// Turns messages somebody else sent to a chat that is not muted into the form a push carries,
/// for a platform that notifies about them while off the screen.
/// </summary>
/// <param name="connection">The coordinator session and its id, or null while not connected.</param>
/// <param name="findChat">The chat of the list by its id.</param>
public sealed partial class ChatPushReporter(
    Func<(ICoordinatorSession Session, string CoordinatorId)?> connection,
    Func<int, ChatSessionVM?> findChat,
    ILogger<ChatPushReporter> logger)
{
    private readonly ILogger _logger = logger;

    /// <summary>
    /// Raised on the UI thread.
    /// </summary>
    public event Action<PushPayload>? Reported;

    // Raised on the transport thread
    public void OnTextMessageAdded(int chatId, ChatMessageModel message)
    {
        if (Reported == null || message.IsSystem)
            return;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (connection() is not ({ } session, { } coordinatorId))
                    return;

                var currentUserId = session.GetCurrentUser().Id;
                if (message.SenderUserId == currentUserId)
                    return;

                // A mention is reported also from a muted chat
                bool isMention = message.Mentions(currentUserId);
                if (findChat(chatId) is not { } chat || (chat.IsMuted && !isMention))
                    return;

                var sender = await session.GetUser(message.SenderUserId);
                // The notification shows the mentions as names, like the chat does
                var text = message.TextContent?.Text;
                if (text != null)
                {
                    IReadOnlyDictionary<int, UserModel> names = message.MentionedUserIds.Count > 0
                        ? await session.GetUsers(message.MentionedUserIds)
                        : new Dictionary<int, UserModel>();
                    text = ChatMessageBody.Create(text, id => names.GetValueOrDefault(id)?.Username, currentUserId,
                        Strings.Get("chat.unknownUser")).Text;
                }
                var attachment = message.Images.Count > 0 ? PushAttachmentKind.Image
                    : message.Video.Count > 0 ? PushAttachmentKind.Video
                    : message.Files.Count > 0 ? PushAttachmentKind.File
                    : PushAttachmentKind.None;

                // No time is passed: the message has just come
                Reported?.Invoke(new PushPayload(coordinatorId, chatId, chat.Model.Name, message.Id,
                    message.SenderUserId, sender?.Username ?? string.Empty, text, attachment, 0,
                    sender?.AvatarUrl, isMention));
            }
            catch (Exception exception)
            {
                LogReportFailed(exception);
            }
        });
    }

    [LoggerMessage(LogLevel.Warning, "Failed to report an incoming chat message")]
    private partial void LogReportFailed(Exception exception);
}
