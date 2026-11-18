using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Reactive.Linq;
using Client.Views.Common;
using Client.ViewModels.Sessions.Chat;

namespace Client.Views.Sessions.Chat;

internal partial class ChatAttachmentList : ViewControl<ChatComposerVM>
{
    public ChatAttachmentList() => InitializeComponent();

    // A click on a card opens the same menu as a right click; the remove button of the card does its own
    private void OnAttachmentTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control card || e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) != null)
            return;

        e.Handled = true;
        card.ContextFlyout?.ShowAt(card);
    }

    private void OnDetachClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ChatAttachmentVM attachment } && DataContext is ChatComposerVM composer)
            composer.RemoveAttachmentCommand.Execute(attachment).Subscribe();
    }
}
