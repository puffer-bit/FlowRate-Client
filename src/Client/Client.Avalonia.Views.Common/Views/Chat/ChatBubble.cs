using Avalonia.Data.Converters;

namespace Client.Views.Shared.Chat;

/// <summary>
/// Sizes of a message drawn as a bubble that the views compute from the message.
/// </summary>
public static class ChatBubble
{
    // The left and right padding of Border.msg-bubble together (ChatMessageViewStyle)
    private const double HorizontalPadding = 20;

    /// <summary>The width of a bubble whose album, of the given width, meets its edges.</summary>
    public static readonly IValueConverter WidthOfAlbum =
        new FuncValueConverter<double, double>(albumWidth => albumWidth + HorizontalPadding);
}
