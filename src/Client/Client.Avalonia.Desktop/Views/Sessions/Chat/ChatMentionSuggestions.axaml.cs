using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Client.Views.Common;
using Client.ViewModels.Sessions.Chat;

namespace Client.Views.Sessions.Chat;

internal partial class ChatMentionSuggestions : ViewControl<ChatMentionPickerVM>
{
    public event EventHandler<ChatMentionSuggestionVM>? SuggestionPicked;

    public ChatMentionSuggestions() => InitializeComponent();

    public void ScrollSelectionIntoView(int index) => MentionItems.ContainerFromIndex(index)?.BringIntoView();

    private void OnMentionPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: ChatMentionSuggestionVM suggestion })
            ViewModel?.SelectSuggestion(suggestion);
    }

    private void OnMentionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ChatMentionSuggestionVM suggestion })
            SuggestionPicked?.Invoke(this, suggestion);
    }
}
