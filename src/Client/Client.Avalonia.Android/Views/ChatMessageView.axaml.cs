using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Linq;
using Client.Avalonia.Styles.Animation;
using Client.ViewModels.Sessions.Chat;

namespace Client.Droid.Views;

public partial class ChatMessageView : UserControl
{
    public ChatMessageView()
    {
        InitializeComponent();
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not ChatMessageVM vm)
            return;
        if (vm.IsSystem) { e.Handled = true; return; }
        if (ChatUserMenu.TryOpenContext(vm, SenderAvatar, NameTextBlock, MessageTextBlock, e))
            return;

        vm.CopyImageTarget = (e.Source as global::Avalonia.Visual)?.GetSelfAndVisualAncestors()
            .OfType<Control>().Select(control => control.DataContext).OfType<ChatImageVM>().FirstOrDefault();
        vm.CopyLinkText = e.TryGetPosition(MessageTextBlock, out var position)
            ? ChatMessageText.GetLinkAt(MessageTextBlock, position)
            : null;
    }

    private void OnNameTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ChatMessageVM vm)
        {
            ChatUserMenu.Open(NameTextBlock, vm, vm.Message.SenderUserId, vm.SenderName, vm.IsSenderDeleted);
            e.Handled = true;
        }
    }

    private void OnAvatarTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ChatMessageVM vm)
        {
            vm.OpenSenderProfile();
            e.Handled = true;
        }
    }

    // Views are recycled while scrolling, so the flag lives in the view model and is cleared on the first play
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is ChatMessageVM vm && vm.TakeLiveArrival())
        {
            // A new message fades in while rising into its place from slightly below
            Presence.Play(this, AppearEffect.Rise);
        }
    }
}
