using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Client.Avalonia.Controls.Avatars;
using Client.Avalonia.Controls.Images;
using Client.ViewModels.Sessions.Chat;
using Client.Views.Shared.Chat;
using Client.Views.Sessions.Chat;

namespace Client.Views.Common;

internal static class DesktopImagePreview
{
    public static void Initialize()
    {
        ChatLinkPreviewCard.ImageOpener = OpenLink;
        InputElement.TappedEvent.AddClassHandler<UserAvatar>((avatar, e) =>
        {
            if (avatar.Picture == null || avatar.Overlay?.IsVisible == true ||
                avatar.GetVisualAncestors().Any(parent => parent is AvatarEditor) ||
                (e.Source as Visual)?.GetSelfAndVisualAncestors().TakeWhile(parent => parent != avatar)
                    .Any(parent => parent is Button) == true)
                return;
            OpenAvatar(avatar);
            e.Handled = true;
        }, RoutingStrategies.Bubble);
    }

    private static async void OpenAvatar(UserAvatar avatar)
    {
        // AvatarService shares cached images; holding a reference keeps the picture alive.
        try
        {
            IImage? original = null;
            if (avatar.AvatarUrl is { } url && UserAvatar.Loader is { } loader)
                original = await loader(avatar.Coordinator, url);
            var picture = original ?? avatar.Picture;
            if (picture != null)
                Open(avatar, picture, avatar.UserName ?? "", true);
        }
        catch
        {
            if (avatar.Picture is { } picture)
                Open(avatar, picture, avatar.UserName ?? "", true);
        }
    }

    private static void OpenLink(Control anchor, ChatLinkPreviewVM preview)
    {
        if (preview.Image is not { } image)
            return;
        preview.Show();
        if (!Open(anchor, image, preview.Title ?? preview.SiteName ?? "", false, preview.Hide))
            preview.Hide();
    }

    private static bool Open(Control anchor, IImage source, string title, bool fullscreen, Action? released = null)
    {
        if (TopLevel.GetTopLevel(anchor) is not Window owner)
            return false;
        // Chat pictures cover their chat, leaving session navigation and the rest of the app in place.
        var chat = anchor.GetSelfAndVisualAncestors().OfType<ChatSessionControl>().FirstOrDefault();
        var host = chat?.Content as Panel ?? owner.Content as Panel;
        if (host == null) return false;
        var previousFocus = owner.FocusManager?.GetFocusedElement();
        var viewer = new ImagePreviewView { Focusable = true };
        var closed = false;
        ImagePreviewVM? model = null;
        void Close()
        {
            if (closed) return;
            closed = true;
            owner.Closed -= OwnerClosed;
            host.DetachedFromVisualTree -= HostDetached;
            host.Children.Remove(viewer);
            model?.Dispose();
            released?.Invoke();
            previousFocus?.Focus();
        }
        void OwnerClosed(object? sender, EventArgs e) => Close();
        void HostDetached(object? sender, VisualTreeAttachmentEventArgs e) => Close();
        model = new ImagePreviewVM(viewer, source, title, Close);
        viewer.DataContext = model;
        viewer.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        owner.Closed += OwnerClosed;
        host.DetachedFromVisualTree += HostDetached;
        if (host is Grid grid)
        {
            Grid.SetRowSpan(viewer, Math.Max(1, grid.RowDefinitions.Count));
            Grid.SetColumnSpan(viewer, Math.Max(1, grid.ColumnDefinitions.Count));
        }
        host.Children.Add(viewer);
        viewer.Focus();
        if (fullscreen) viewer.EnterFullscreen();
        return true;
    }
}
