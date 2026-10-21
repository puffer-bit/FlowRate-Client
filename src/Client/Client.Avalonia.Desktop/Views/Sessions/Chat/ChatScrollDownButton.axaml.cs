using Avalonia.Controls;

namespace Client.Views.Sessions.Chat;

/// <summary>
/// The button over the corner of the message list that goes back to the newest message, with the
/// number of messages that came in below.
/// </summary>
internal partial class ChatScrollDownButton : UserControl
{
    public ChatScrollDownButton()
    {
        InitializeComponent();
    }
}
