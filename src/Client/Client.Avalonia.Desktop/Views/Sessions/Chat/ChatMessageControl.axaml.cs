using System;
using System.Linq;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Client.Avalonia.Styles.Animation;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.Views.Common;
using Client.Views.Dialogs;
using Client.ViewModels.Dialogs;
using Client.Avalonia.Localization;
using Client.ViewModels.Sessions.Chat;

namespace Client.Views.Sessions.Chat;

public partial class ChatMessageControl : ViewControl<ChatMessageVM>
{
    public ChatMessageControl()
    {
        InitializeComponent();

        // On the way down, so the link under the pointer is known before the flyout opens
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
        NameTextBlock.AddHandler(TappedEvent, OnNameTapped, handledEventsToo: true);

        this.WhenActivated(disposables =>
        {
            ViewModel!.CloseFlyoutInteraction.RegisterHandler(async interaction =>
            {
                MessageView.ContextFlyout?.Hide();
                
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);
            
            ViewModel!.CopyMessageInteraction.RegisterHandler(async interaction =>
            {
                await ClipboardAccess.SetTextAsync(this, interaction.Input);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel!.CopyImageInteraction.RegisterHandler(async interaction =>
            {
                bool copied = false;
                try
                {
                    using var bitmap = await interaction.Input.LoadOriginalBitmapAsync();
                    if (bitmap != null)
                        copied = await ClipboardAccess.SetBitmapAsync(this, bitmap);
                }
                catch (Exception)
                {
                    // Loading and platform clipboard failures are reported together below.
                }

                if (!copied)
                    await MessageBoxInteraction.ShowAsync(new MessageBoxVM(Icon.Error, Buttons.Ok,
                        Strings.Get("chat.image.copyFailed"), Strings.Get("chat.menu.copyImage")), this);

                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);
        });
    }

    // Views are recycled while scrolling, so the flag lives in the view model and is cleared on the first play
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is ChatMessageVM { IsNew: true } vm)
        {
            vm.IsNew = false;
            // A new message fades in while rising into its place from slightly below
            Presence.Play(this, AppearEffect.Rise);
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

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is ChatMessageVM vm)
        {
            if (ChatUserMenu.TryOpenContext(vm, SenderAvatar, NameTextBlock, MessageTextBlock, e))
                return;

            vm.CopyImageTarget = (e.Source as Visual)?.GetSelfAndVisualAncestors()
                .OfType<Control>().Select(control => control.DataContext).OfType<ChatImageVM>().FirstOrDefault();
            vm.ContextFile = (e.Source as Visual)?.GetSelfAndVisualAncestors()
                .OfType<Control>().Select(control => control.DataContext).OfType<ChatFileVM>().FirstOrDefault();

            vm.CopyLinkText = e.TryGetPosition(MessageTextBlock, out var position)
                ? ChatMessageText.GetLinkAt(MessageTextBlock, position)
                : null;
        }
    }

    private void OnNameTapped(object? sender, TappedEventArgs e)
    {
        if (NameTextBlock.SelectionStart != NameTextBlock.SelectionEnd)
            return;

        if (DataContext is ChatMessageVM vm)
        {
            ChatUserMenu.Open(NameTextBlock, vm, vm.Message.SenderUserId, vm.SenderName, vm.IsSenderDeleted);
            e.Handled = true;
        }
    }

    private void FlyoutBase_OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not ChatMessageVM vm)
            return;

        vm.IsFlyoutOpen = true;

        // A selection over several messages is copied whole from any of them
        if (this.FindAncestorOfType<ChatSessionControl>()?.Selection is { HasSelection: true } selection)
        {
            vm.SetCopyText(selection.GetText(), true);
            return;
        }

        int savedStart = MessageTextBlock.SelectionStart;
        int savedEnd = MessageTextBlock.SelectionEnd;
        
        bool hasSelection = MessageTextBlock.SelectionStart != MessageTextBlock.SelectionEnd;
        // The text block holds runs when the message has links, so its own Text may be empty
        string? fullText = ChatMessageText.GetText(MessageTextBlock);

        if (string.IsNullOrEmpty(fullText))
        {
            vm.SetCopyText(null, false);
            return;
        }

        string textToSend;

        if (hasSelection)
        {
            int start = Math.Min((int)MessageTextBlock.SelectionStart, (int)MessageTextBlock.SelectionEnd);
            int end = Math.Max((int)MessageTextBlock.SelectionStart, (int)MessageTextBlock.SelectionEnd);

            textToSend = fullText.Substring(start, end - start);
        }
        else
        {
            textToSend = fullText;
        }

        vm.SetCopyText(textToSend, hasSelection);

        Dispatcher.UIThread.Post(() =>
        {
            MessageTextBlock.Focus();
            MessageTextBlock.SelectionStart = savedStart;
            MessageTextBlock.SelectionEnd = savedEnd;
        }, DispatcherPriority.Background);
    }
    
    private void FlyoutBase_OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is ChatMessageVM vm)
        {
            vm.IsFlyoutOpen = false;
        }
    }
}
