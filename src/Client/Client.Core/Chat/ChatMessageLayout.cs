namespace Client.Core.Chat;

/// <summary>How the messages of a chat are drawn; stored by its number.</summary>
public enum ChatMessageLayout
{
    /// <summary>One under another with the author's avatar and name, as in Discord.</summary>
    List = 0,

    /// <summary>In bubbles, the user's own on the right, with the time inside, as in Telegram.</summary>
    Bubbles = 1
}
