using System;
using Avalonia.Controls;
using Client.Core.Chat;
using Client.ViewModels.Sessions.Chat;

namespace Client.Views.Shared.Chat;

public partial class ChatReactionPicker : UserControl
{
    private readonly RecentEmoji _recent = new();
    private Flyout? _pickerFlyout;

    public ChatReactionPicker()
    {
        InitializeComponent();
        AllEmoji.EmojiPicked += async (_, e) =>
        {
            if (DataContext is not ChatMessageVM message || !message.CanReact)
                return;
            e.Accepted = true;
            await message.ChooseReactionAsync(e.Entry.Text);
        };
        AllEmoji.CloseRequested += (_, _) => _pickerFlyout?.Hide();
    }

    private void OnPickerOpened(object? sender, EventArgs e)
    {
        _pickerFlyout = sender as Flyout;
        AllEmoji.Open(_recent);
    }
}
