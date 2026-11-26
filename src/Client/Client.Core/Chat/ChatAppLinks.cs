using System.Text.RegularExpressions;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Links;

namespace Client.Core.Chat;

/// <summary>
/// Finds the <c>flowrate://</c> links of the application in the text of a message. They are found on the client only:
/// the server never sees them as links and makes no preview of them. What they ask for is read by the application's
/// link parser; this only says where they sit.
/// </summary>
public static partial class ChatAppLinks
{
    public const string Scheme = "flowrate";

    // The action of a link that approves a device: it never opens from a message, only its card warns about it
    private const string DeviceLinkAction = "link";

    // Punctuation that ends a sentence rather than a link
    private const string TrailingPunctuation = ".,;:!?'\"*…»”’)]}";

    // A link starts at a word boundary, as a web address does
    [GeneratedRegex(@"(?<![\p{L}\p{N}@./:_-])flowrate://[^\s<>""]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Candidate();

    /// <summary>The links that open by a click: all of them but the approvals of a device.</summary>
    public static IReadOnlyList<ChatLink> Find(string? text) =>
        FindAll(text).Where(link => !IsDeviceLink(link.Uri)).ToArray();

    /// <summary>Every link of the application in the text, in the order they are written.</summary>
    public static IReadOnlyList<ChatLink> FindAll(string? text)
    {
        // Most messages have none, and the pattern is not run on them
        if (string.IsNullOrEmpty(text) || !text.Contains(Scheme + "://", StringComparison.OrdinalIgnoreCase))
            return [];

        List<ChatLink>? links = null;
        foreach (Match match in Candidate().Matches(text))
        {
            int length = match.Length;
            while (length > 0 && TrailingPunctuation.Contains(text[match.Index + length - 1]))
                length--;

            string address = text.Substring(match.Index, length);
            if (address.Length <= Scheme.Length + 3
                || !Uri.TryCreate(address, UriKind.Absolute, out var uri)
                || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
                continue;

            (links ??= []).Add(new ChatLink(match.Index, length, uri));
        }

        return links ?? (IReadOnlyList<ChatLink>)[];
    }

    /// <summary>Whether the address is a link of the application rather than of the web.</summary>
    public static bool IsAppLink(Uri uri) =>
        uri.IsAbsoluteUri && string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase);

    private static bool IsDeviceLink(Uri uri)
    {
        var rest = uri.OriginalString[(Scheme.Length + 1)..].TrimStart('/');
        int end = rest.IndexOfAny(['?', '/', '#']);
        var action = end < 0 ? rest : rest[..end];
        return string.Equals(action, DeviceLinkAction, StringComparison.OrdinalIgnoreCase);
    }
}
