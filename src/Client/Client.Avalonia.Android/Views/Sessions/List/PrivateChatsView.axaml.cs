using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Client.ViewModels.Sessions.List;

namespace Client.Droid.Views.Sessions.List;

public partial class PrivateChatsView : UserControl
{
    private bool _isMenuPress;

    public PrivateChatsView()
    {
        InitializeComponent();
        Chats.AddHandler(ContextRequestedEvent, OnChatContextRequested);
        Chats.AddHandler(PointerPressedEvent, (_, _) => _isMenuPress = false, RoutingStrategies.Tunnel);
        Chats.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (_isMenuPress)
                e.Handled = true;
            _isMenuPress = false;
        }, RoutingStrategies.Tunnel);
    }

    public event Action<ChatSessionVM>? ChatRequested;
    public event Action<ChatSessionVM>? ChatMenuRequested;

    private void OnChatTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not StyledElement { DataContext: ChatSessionVM chat } || _isMenuPress)
            return;

        ChatRequested?.Invoke(chat);
        e.Handled = true;
    }

    private void OnChatContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not StyledElement { DataContext: ChatSessionVM chat })
            return;

        ChatMenuRequested?.Invoke(chat);
        _isMenuPress = true;
        e.Handled = true;
    }
}
