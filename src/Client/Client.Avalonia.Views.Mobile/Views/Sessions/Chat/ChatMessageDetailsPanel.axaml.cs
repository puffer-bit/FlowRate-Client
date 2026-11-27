using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Client.ViewModels.Sessions.Chat;

namespace Client.Mobile.Views.Sessions.Chat;

/// <summary>
/// The whole list of who read a message or who reacted to it, opened from its island in the message menu.
/// </summary>
public partial class ChatMessageDetailsPanel : UserControl
{
    public ChatMessageDetailsPanel()
    {
        InitializeComponent();
        ShowsReactions = false;
    }

    public event Action? CloseRequested;

    /// <summary>Shows who reacted rather than who read the message.</summary>
    public bool ShowsReactions
    {
        get => ReactionsList.IsVisible;
        set
        {
            ReadersTitle.IsVisible = ReadersState.IsVisible = ReadersList.IsVisible = !value;
            ReactionsTitle.IsVisible = ReactionsList.IsVisible = value;
        }
    }

    private void OnBackClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    // The menu of the user; what is picked in it leaves the chat or acts on it, so the page goes away
    private void OnUserPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control row)
            ChatUserMenu.OpenForReader(row, e, () => CloseRequested?.Invoke());
    }
}
