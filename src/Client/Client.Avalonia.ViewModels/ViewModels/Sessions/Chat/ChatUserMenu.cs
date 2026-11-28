using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Media;
using Client.Avalonia.Controls.Menus;
using Client.Avalonia.Localization;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>The same user actions for authors and mentions on both clients.</summary>
public static class ChatUserMenu
{
    /// <param name="picked">Called once an action is picked, e.g. to close the menu the target is in.</param>
    public static void Open(Control target, ChatMessageVM message, int userId, string? name, bool isDeleted = false,
        Action? picked = null)
    {
        var profile = new MenuItemButton
        {
            Content = Strings.Get("peer.viewProfile"),
            Icon = (IImage?)target.FindResource("UserIcon"),
            Gesture = new KeyGesture(Key.P),
            IsEnabled = !isDeleted
        };
        var direct = new MenuItemButton
        {
            Content = Strings.Get("privateChats.write"),
            Icon = (IImage?)target.FindResource("SendIcon"),
            IsVisible = !isDeleted && message.CanOpenDirectChat(userId)
        };
        var mention = new MenuItemButton
        {
            Content = Strings.Get("chat.menu.mention"),
            Icon = (IImage?)target.FindResource("MentionIcon"),
            IsEnabled = !isDeleted && !string.IsNullOrEmpty(name) && message.CanMentionUser
        };

        var menu = new Flyout
        {
            Content = new StackPanel { Spacing = 2, Children = { mention, direct, profile } }
        };
        profile.Click += (_, _) =>
        {
            menu.Hide();
            picked?.Invoke();
            message.OpenUserProfile(userId);
        };
        direct.Click += (_, _) =>
        {
            menu.Hide();
            picked?.Invoke();
            message.OpenDirectChat(userId);
        };
        mention.Click += (_, _) =>
        {
            menu.Hide();
            picked?.Invoke();
            message.MentionUser(userId, name!);
        };
        menu.ShowAt(target, true);
    }

    /// <summary>
    /// A row of the readers of a message, or of who reacted to it, was pressed, by any button or a tap: the menu
    /// of that user.
    /// </summary>
    public static void OpenForReader(Control row, PointerReleasedEventArgs e, Action? picked = null)
    {
        // A reader of the message, or a user who reacted to it
        var user = row.DataContext switch
        {
            ChatMessageReaderVM reader => (reader.UserId, reader.Name),
            ChatReactionDetailVM { UserId: > 0 } reaction => (reaction.UserId, reaction.Name),
            _ => ((int, string)?)null
        };
        if (user is not { } who
            || row.GetVisualAncestors().OfType<Control>().Select(c => c.DataContext)
                .OfType<ChatMessageVM>().FirstOrDefault() is not { } message)
            return;

        Open(row, message, who.Item1, who.Item2, picked: picked);
        e.Handled = true;
    }

    public static bool TryOpenContext(ChatMessageVM message, Control avatar, Control name, TextBlock text,
        ContextRequestedEventArgs e, double reach = 0)
    {
        foreach (var target in new[] { avatar, name })
        {
            if (target.IsEffectivelyVisible && e.TryGetPosition(target, out var position)
                && new global::Avalonia.Rect(target.Bounds.Size).Contains(position))
            {
                Open(target, message, message.Message.SenderUserId, message.SenderName, message.IsSenderDeleted);
                e.Handled = true;
                return true;
            }
        }

        if (e.TryGetPosition(text, out var textPosition)
            && ChatMessageText.GetMentionAt(text, textPosition, reach) is { UserId: { } userId } span)
        {
            var body = ChatMessageText.GetText(text)!;
            Open(text, message, userId, body.Substring(span.Start + 1, span.Length - 1));
            e.Handled = true;
            return true;
        }

        return false;
    }
}
