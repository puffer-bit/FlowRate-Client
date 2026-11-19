using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Client.Droid.Views.Sessions.Chat;

public partial class ChatViewersPanel : UserControl
{
    public ChatViewersPanel() => InitializeComponent();

    public event Action? CloseRequested;

    private void OnBackClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
