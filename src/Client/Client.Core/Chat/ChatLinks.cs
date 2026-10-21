using System.Text.RegularExpressions;

namespace Client.Core.Chat;

/// <summary>
/// A web address found in the text of a message: where it sits in the text and what it opens.
/// </summary>
public readonly record struct ChatLink(int Start, int Length, Uri Uri)
{
    public int End => Start + Length;
}

/// <summary>
/// Finds web addresses in the text of a message. Only http and https count, written out in full
/// or starting with "www."; the text itself is never changed.
/// </summary>
public static partial class ChatLinks
{
    // Punctuation that ends a sentence rather than an address
    private const string TrailingPunctuation = ".,;:!?'\"*…»”’";

    // An address starts at a word boundary, so "xhttp://a" and "mail@www.a.b" are left alone
    [GeneratedRegex(@"(?<![\p{L}\p{N}@./:_-])(?:https?://|www\.)[^\s<>""]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Candidate();

    public static IReadOnlyList<ChatLink> Find(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        List<ChatLink>? links = null;

        foreach (Match match in Candidate().Matches(text))
        {
            int length = TrimEnd(text.AsSpan(match.Index, match.Length));
            if (length == 0)
                continue;

            string address = text.Substring(match.Index, length);
            if (address.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                address = "https://" + address;

            // The scheme is checked again: the pattern ignores case, and nothing but the web may open
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || uri.Host.Length == 0)
                continue;

            (links ??= []).Add(new ChatLink(match.Index, length, uri));
        }

        return links ?? (IReadOnlyList<ChatLink>)[];
    }

    // Cuts what belongs to the sentence around the address: the final punctuation and a closing
    // bracket that has no opening one inside the address, as in "(see https://a.b/c)"
    private static int TrimEnd(ReadOnlySpan<char> address)
    {
        int length = address.Length;

        while (length > 0)
        {
            char last = address[length - 1];

            if (TrailingPunctuation.Contains(last))
            {
                length--;
                continue;
            }

            char opening = last switch
            {
                ')' => '(',
                ']' => '[',
                '}' => '{',
                _ => '\0'
            };
            if (opening == '\0')
                break;

            var body = address[..length];
            if (body.Count(opening) >= body.Count(last))
                break;

            length--;
        }

        return length;
    }
}
