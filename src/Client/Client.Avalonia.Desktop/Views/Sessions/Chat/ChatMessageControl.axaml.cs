using System;
using System.Reactive.Disposables;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Client.Avalonia.Styles.Animation;
using ReactiveUI;
using ReactiveUI.Primitives;
using Client.Views.Common;
using Client.ViewModels.Sessions.Chat;

namespace Client.Views.Sessions.Chat;

public partial class ChatMessageControl : ViewControl<ChatMessageVM>
{
    public ChatMessageControl()
    {
        InitializeComponent();

        // On the way down, so the link under the pointer is known before the flyout opens
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);

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

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is ChatMessageVM vm)
            vm.CopyLinkText = e.TryGetPosition(MessageTextBlock, out var position)
                ? ChatMessageText.GetLinkAt(MessageTextBlock, position)
                : null;
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

        if (string.IsNullOrEmpty(fullText)) return;

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