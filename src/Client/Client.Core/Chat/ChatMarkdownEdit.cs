namespace Client.Core.Chat;

public enum ChatMarkdownFormat
{
    Bold,
    Italic,
    Strike,
    Code,
    CodeBlock,
    Link
}

/// <summary>A single TextBox replacement, preserving its undo history and the selected content.</summary>
public sealed record ChatMarkdownEdit(int Start, int End, string Replacement, int SelectionStart, int SelectionEnd)
{
    public static ChatMarkdownEdit Create(string text, int selectionStart, int selectionEnd, ChatMarkdownFormat format,
        string linkLabel = "link")
    {
        int start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, text.Length);
        int end = Math.Clamp(Math.Max(selectionStart, selectionEnd), start, text.Length);
        string selected = text[start..end];
        string open;
        string close;
        switch (format)
        {
            case ChatMarkdownFormat.Bold:
                open = close = "**";
                break;
            case ChatMarkdownFormat.Italic:
                open = close = "*";
                break;
            case ChatMarkdownFormat.Strike:
                open = close = "~~";
                break;
            case ChatMarkdownFormat.Code:
                open = close = new string('`', Math.Max(1, LongestBackticks(selected) + 1));
                // CommonMark strips one padding space when needed to separate content from the delimiter.
                if (selected.StartsWith('`') || selected.EndsWith('`')
                    || (selected.StartsWith(' ') && selected.EndsWith(' ') && selected.Trim(' ').Length > 0))
                {
                    open += " ";
                    close = " " + close;
                }
                break;
            case ChatMarkdownFormat.CodeBlock:
                string fence = new('`', Math.Max(3, LongestBackticks(selected) + 1));
                open = (start > 0 && text[start - 1] != '\n' ? "\n" : "") + fence + "\n";
                close = (selected.EndsWith('\n') ? "" : "\n") + fence
                    + (end < text.Length && text[end] != '\n' ? "\n" : "");
                break;
            case ChatMarkdownFormat.Link:
                return CreateLink(start, end, selected, linkLabel);
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }

        // Applying the same inline style again removes its surrounding delimiters.
        bool canToggle = format is ChatMarkdownFormat.Bold or ChatMarkdownFormat.Italic or ChatMarkdownFormat.Strike;
        if (canToggle && IsWrappedSelection(text, start, end, open, close)
            && (format != ChatMarkdownFormat.Italic || HasItalicDelimiters(text, start, end)))
        {
            int unwrappedStart = start - open.Length;
            return new ChatMarkdownEdit(unwrappedStart, end + close.Length, selected,
                unwrappedStart, unwrappedStart + selected.Length);
        }

        int contentStart = start + open.Length;
        return new ChatMarkdownEdit(start, end, open + selected + close, contentStart, contentStart + selected.Length);
    }

    private static ChatMarkdownEdit CreateLink(int start, int end, string selected, string linkLabel)
    {
        const string address = "https://";
        string label = (selected.Length == 0 ? linkLabel : selected)
            .Replace("\\", "\\\\")
            .Replace("[", "\\[")
            .Replace("]", "\\]");
        string replacement = "[" + label + "](" + address + ")";
        int addressStart = start + label.Length + 3;
        return new ChatMarkdownEdit(start, end, replacement, addressStart, addressStart + address.Length);
    }

    private static bool IsWrappedSelection(string text, int start, int end, string open, string close) =>
        start >= open.Length && end + close.Length <= text.Length
        && text.AsSpan(start - open.Length, open.Length).SequenceEqual(open)
        && text.AsSpan(end, close.Length).SequenceEqual(close);

    private static bool HasItalicDelimiters(string text, int start, int end)
    {
        int left = start;
        int right = end;
        while (left > 0 && text[left - 1] == '*')
            left--;
        while (right < text.Length && text[right] == '*')
            right++;
        // Two asterisks mean bold; three include an italic delimiter.
        return Math.Min(start - left, right - end) % 2 == 1;
    }

    private static int LongestBackticks(string text)
    {
        int longest = 0;
        int count = 0;
        foreach (char character in text)
        {
            count = character == '`' ? count + 1 : 0;
            longest = Math.Max(longest, count);
        }
        return longest;
    }
}
