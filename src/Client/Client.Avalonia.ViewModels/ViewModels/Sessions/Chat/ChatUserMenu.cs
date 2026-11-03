using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Client.Avalonia.Controls.Menus;
using Client.Avalonia.Localization;

namespace Client.ViewModels.Sessions.Chat;

/// <summary>The same user actions for authors and mentions on both clients.</summary>
public static class ChatUserMenu
{
    public static void Open(Control target, ChatMessageVM message, int userId, string? name, bool isDeleted = false)
    {
        var profile = new MenuItemButton
        {
            Content = Strings.Get("peer.viewProfile"),
            Icon = (IImage?)target.FindResource("UserIcon"),
            Gesture = new KeyGesture(Key.P),
            IsEnabled = !isDeleted
        };
        var mention = new MenuItemButton
        {
            Content = Strings.Get("chat.menu.mention"),
            Icon = (IImage?)target.FindResource("MentionIcon"),
            IsEnabled = !isDeleted && !string.IsNullOrEmpty(name) && message.CanMentionUser
        };

        var menu = new Flyout
        {
            Content = new StackPanel { Spacing = 2, Children = { profile, mention } }
        };
        profile.Click += (_, _) =>
        {
            menu.Hide();
            message.OpenUserProfile(userId);
        };
        mention.Click += (_, _) =>
        {
            menu.Hide();
            message.MentionUser(userId, name!);
        };
        menu.ShowAt(target, true);
    }

    public static bool TryOpenContext(ChatMessageVM message, Control avatar, Control name, TextBlock text,
        ContextRequestedEventArgs e)
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
            && ChatMessageText.GetMentionAt(text, textPosition) is { UserId: { } userId } span)
        {
            var body = ChatMessageText.GetText(text)!;
            Open(text, message, userId, body.Substring(span.Start + 1, span.Length - 1));
            e.Handled = true;
            return true;
        }

        return false;
    }
}
