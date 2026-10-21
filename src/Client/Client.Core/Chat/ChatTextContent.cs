using System.Text;
using Shared.Validation;

namespace Client.Core.Chat;

public class ChatTextContent
{
    /// <summary>
    /// The longest text a message may carry, in UTF-16 code units (what <see cref="string.Length"/> counts).
    /// </summary>
    public const int MaxLength = Limits.ChatTextMaxLength;

    // More blank lines in a row than this are squeezed down to it
    private const int MaxConsecutiveLineBreaks = 2;

    public required string Text { get; init; }

    /// <summary>
    /// Builds the content of an outgoing message from what the user typed: the text is normalized
    /// with <see cref="Normalize"/>. Returns null when nothing visible is left or the text is longer
    /// than <see cref="MaxLength"/>. Incoming messages are not passed through here.
    /// </summary>
    public static ChatTextContent? FromUserInput(string? text)
    {
        var normalized = Normalize(text);
        return normalized.Length is > 0 and <= MaxLength
            ? new ChatTextContent { Text = normalized }
            : null;
    }

    /// <summary>
    /// Brings line breaks to '\n', drops control characters and zero-width spaces, squeezes runs of
    /// blank lines and trims the edges. Returns an empty string when nothing visible is left.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        int lineBreaks = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '\r')
            {
                // "\r\n" counts as one break
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    continue;
                c = '\n';
            }

            if (c == '\n')
            {
                if (++lineBreaks <= MaxConsecutiveLineBreaks)
                    builder.Append(c);
                continue;
            }

            if (char.IsWhiteSpace(c) && c != '\t' && c != ' ')
                c = ' ';
            else if (char.IsControl(c) && c != '\t')
                continue;
            else if (IsInvisibleFormat(c))
                continue;

            // Spaces between line breaks do not end a run of blank lines
            if (c != ' ' && c != '\t')
                lineBreaks = 0;
            builder.Append(c);
        }

        return builder.ToString().Trim();
    }

    // Zero-width spaces and the BOM carry nothing and let a message look empty.
    // Joiners (U+200C, U+200D) and bidi marks stay: Persian, Arabic and emoji sequences need them
    private static bool IsInvisibleFormat(char c) => c is (char)0x200B or (char)0x2060 or (char)0xFEFF;
}
