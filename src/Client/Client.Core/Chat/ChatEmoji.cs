using System.Globalization;

namespace Client.Core.Chat;

/// <summary>
/// A piece of the text of a message that asks to be drawn as a color emoji.
/// </summary>
public readonly record struct ChatEmojiSpan(int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>
/// Finds the emoji that a text font would draw as a black and white symbol. A character such as
/// ❤ or ☀ is in many text fonts, so the font of the message draws it and the emoji font is never
/// asked; the variation selector U+FE0F after it says it is meant as an emoji. Emoji missing from
/// the text font need nothing: the font fallback finds them.
/// </summary>
public static class ChatEmoji
{
    /// <summary>
    /// The most emoji a message may hold to be shown large.
    /// </summary>
    public const int MaxJumbo = 3;

    private const char EmojiPresentation = '\uFE0F';
    private const char TextPresentation = '\uFE0E';
    private const char Keycap = '\u20E3';

    /// <summary>
    /// True when the text is nothing but one to <see cref="MaxJumbo"/> emoji, spaces between them
    /// allowed: such a message is shown large, as a picture rather than a line of text.
    /// </summary>
    public static bool IsJumbo(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        int count = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);

        while (elements.MoveNext())
        {
            var element = (string)elements.Current;
            if (string.IsNullOrWhiteSpace(element))
                continue;

            if (!IsEmoji(element) || ++count > MaxJumbo)
                return false;
        }

        return count > 0;
    }

    // An approximation of Extended_Pictographic, which .NET does not expose: the blocks of symbols
    // and pictographs, or anything a variation selector or a keycap makes an emoji
    private static bool IsEmoji(string element)
    {
        if (element.Contains(TextPresentation))
            return false;
        if (element.Contains(EmojiPresentation) || element.Contains(Keycap))
            return true;

        int first = char.ConvertToUtf32(element, 0);
        return first is >= 0x1F000 and <= 0x1FAFF // pictographs, emoticons, flags
            or >= 0x2600 and <= 0x27BF // miscellaneous symbols, dingbats
            or >= 0x2300 and <= 0x23FF // watches, alarm clocks, hourglasses
            or >= 0x2B00 and <= 0x2BFF; // stars, squares
    }

    public static IReadOnlyList<ChatEmojiSpan> FindPresentation(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains(EmojiPresentation))
            return [];

        var spans = new List<ChatEmojiSpan>();
        var elements = StringInfo.GetTextElementEnumerator(text);

        // A whole grapheme is taken, so a heart on fire or a keycap stays in one font
        while (elements.MoveNext())
        {
            int start = elements.ElementIndex;
            var element = (string)elements.Current;

            // The selector alone, with nothing before it, is nothing to draw
            if (element.Length < 2 || !element.Contains(EmojiPresentation))
                continue;

            if (spans.Count > 0 && spans[^1].End == start)
                spans[^1] = spans[^1] with { Length = spans[^1].Length + element.Length };
            else
                spans.Add(new ChatEmojiSpan(start, element.Length));
        }

        return spans;
    }
}
