using System;
using System.Collections.Generic;
using System.Linq;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using Shared.Models.DataTransferObjects.ChatSession.Messages;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>Formats localized events without interpreting user names as markup.</summary>
public static class ChatSystemMessageText
{
    public static string Format(ChatMessageModel message, Func<int, string?> getName) =>
        string.Concat(Parts(message, getName).Select(part => part.Text));

    public static IReadOnlyList<ChatSystemMessagePart> Parts(ChatMessageModel message,
        Func<int, string?> getName, Func<int, bool>? canOpenUser = null)
    {
        if (!message.IsSystem) return [];
        var content = message.SystemContent;
        if (content?.IsValid() != true)
            return [new(message.TextContent?.Text ?? Strings.Get("chat.system.event"))];
        var key = content.Kind switch
        {
            ChatSystemEventKind.MemberAdded => "chat.system.memberAdded",
            ChatSystemEventKind.MemberRemoved => "chat.system.memberRemoved",
            ChatSystemEventKind.MemberLeft => "chat.system.memberLeft",
            ChatSystemEventKind.CallStarted => "chat.system.callStarted",
            ChatSystemEventKind.CallEnded => "chat.system.callEnded",
            _ => "chat.system.event"
        };
        if (content.Kind == ChatSystemEventKind.CallEnded && content.CallEndReason is { } reason)
        {
            var outcome = Strings.Get(reason switch
            {
                Shared.Models.DataTransferObjects.DirectCallEndReason.Declined => "calls.declined",
                Shared.Models.DataTransferObjects.DirectCallEndReason.Cancelled => "calls.cancelled",
                Shared.Models.DataTransferObjects.DirectCallEndReason.Missed => "calls.missed",
                Shared.Models.DataTransferObjects.DirectCallEndReason.Disconnected => "calls.disconnected",
                _ => "calls.ended"
            });
            if (content.CallDurationSeconds is { } seconds)
                outcome += " · " + TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"mm\:ss");
            return [new(outcome)];
        }
        // Resolve names after formatting, so names can contain placeholders or punctuation verbatim.
        const string actor = "\uFFF0actor\uFFF1", subject = "\uFFF0subject\uFFF1";
        var text = Strings.Format(key, ("actor", actor), ("subject", subject));
        List<ChatSystemMessagePart> parts = [];
        for (var offset = 0; offset < text.Length;)
        {
            var actorAt = text.IndexOf(actor, offset, StringComparison.Ordinal);
            var subjectAt = text.IndexOf(subject, offset, StringComparison.Ordinal);
            var isActor = actorAt >= 0 && (subjectAt < 0 || actorAt < subjectAt);
            var start = isActor ? actorAt : subjectAt;
            if (start < 0) { parts.Add(new(text[offset..])); break; }
            if (start > offset) parts.Add(new(text[offset..start]));
            var id = isActor ? content.ActorUserId : content.SubjectUserId;
            var name = id is { } userId ? getName(userId) : null;
            var link = name != null && id is { } resolvedId && (canOpenUser?.Invoke(resolvedId) ?? true);
            parts.Add(new(name ?? "…", link ? id : null));
            offset = start + (isActor ? actor.Length : subject.Length);
        }
        return parts;
    }
}
