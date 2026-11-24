using System;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Linq;
using Client.Avalonia.Styles.Animation;
using Client.ViewModels.Sessions.Chat;
using ReactiveUI.Primitives;

namespace Client.Mobile.Views.Sessions.Chat;

public partial class ChatMessageView : UserControl
{
    private readonly SerialDisposable _copySubscription = new();

    public ChatMessageView()
    {
        InitializeComponent();
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
    }

    // Code panels copy directly, before a message menu has ever been opened. Recycled rows must
    // handle their current message only and release the handler when they leave the visual tree.
    private void FollowClipboard()
    {
        _copySubscription.Disposable = this.IsAttachedToVisualTree() && DataContext is ChatMessageVM message
            ? message.CopyMessageInteraction.RegisterHandler(async interaction =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                    await clipboard.SetTextAsync(interaction.Input);
                interaction.SetOutput(RxVoid.Default);
            })
            : null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        FollowClipboard();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _copySubscription.Disposable = null;
        base.OnDetachedFromVisualTree(e);
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
        FollowClipboard();

        if (DataContext is ChatMessageVM vm && vm.TakeLiveArrival())
        {
            // A new message fades in while rising into its place from slightly below
            Presence.Play(this, AppearEffect.Rise);
        }
    }
}
