using System.Runtime.CompilerServices;
using System.Text;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Parsers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Client.Core.Chat;

[Flags]
public enum ChatTextStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Strike = 4,
    Code = 8,
    CodeBlock = 16
}

public readonly record struct ChatStyleSpan(int Start, int Length, ChatTextStyle Style)
{
    public int End => Start + Length;
}

/// <summary>Rendered text and interactive ranges, all in displayed UTF-16 coordinates.</summary>
public sealed record ChatMarkdownDocument(string Text, IReadOnlyList<ChatMentionSpan> Mentions,
    IReadOnlyList<ChatLink> Links, IReadOnlyList<ChatStyleSpan> Styles);

/// <summary>
/// A chat subset of CommonMark: emphasis, strike, code and web links. Other block syntax stays literal.
/// Projection keeps source positions separate from displayed positions, including resolved mention names.
/// </summary>
public static class ChatMarkdown
{
    private static readonly ConditionalWeakTable<ChatMessageBody, ChatMarkdownDocument> Cache = new();
    private static readonly MarkdownPipeline Pipeline = CreatePipeline();

    public static ChatMarkdownDocument Render(ChatMessageBody body) => Cache.GetValue(body, Parse);

    public static ChatMarkdownDocument Parse(ChatMessageBody body) => new Parser(body).Parse();

    private static MarkdownPipeline CreatePipeline()
    {
        var builder = new MarkdownPipelineBuilder()
            .DisableHtml()
            .UsePreciseSourceLocation()
            .UseAutoLinks()
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough);

        // Chat paragraphs retain their line breaks; only fenced code introduces a block format.
        for (int i = builder.BlockParsers.Count - 1; i >= 0; i--)
        {
            if (builder.BlockParsers[i] is not ParagraphBlockParser and not FencedCodeBlockParser)
                builder.BlockParsers.RemoveAt(i);
        }

        return builder.Build();
    }

    private static bool TryWebUri(string? value, out Uri? uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Host.Length > 0
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>Collects source edits, then projects them into displayed text and ranges.</summary>
    private sealed class Parser
    {
        private const char MentionPlaceholder = '\uE000';

        private readonly ChatMessageBody _body;
        private readonly string _source;
        private readonly bool[] _hidden;
        private readonly ChatTextStyle[] _sourceStyles;
        private readonly Dictionary<int, SourceReplacement> _replacements = new();
        private readonly List<ChatLink> _sourceLinks = [];

        public Parser(ChatMessageBody body)
        {
            _body = body;
            _source = body.Text;
            _hidden = new bool[_source.Length];
            _sourceStyles = new ChatTextStyle[_source.Length];
        }

        public ChatMarkdownDocument Parse()
        {
            var document = Markdown.Parse(ProtectMentions(), Pipeline);
            Visit(document);

            var projection = ProjectText();
            var mentions = ProjectMentions(projection);
            var links = ProjectLinks(projection, mentions);
            return new ChatMarkdownDocument(projection.Text, mentions, links, projection.Styles);
        }

        private string ProtectMentions()
        {
            var characters = _source.ToCharArray();
            foreach (var mention in _body.Mentions)
                Array.Fill(characters, MentionPlaceholder, mention.Start, mention.Length);
            return new string(characters);
        }

        private void Visit(MarkdownObject node)
        {
            switch (node)
            {
                case FencedCodeBlock fenced:
                    if (string.IsNullOrWhiteSpace(_source[..node.Span.Start]))
                        Hide(0, node.Span.Start - 1);
                    Replace(node, RestoreMentions(fenced.Lines.ToString(), node.Span), ChatTextStyle.Code | ChatTextStyle.CodeBlock);
                    return;
                case CodeInline code:
                    Replace(node, RestoreMentions(code.Content, node.Span), ChatTextStyle.Code);
                    return;
                case EmphasisInline emphasis:
                    ReadEmphasis(emphasis);
                    break;
                case LinkInline link:
                    if (!ReadLink(link))
                        return;
                    break;
                case HtmlEntityInline entity:
                    Replace(node, entity.Transcoded.ToString());
                    return;
                case LiteralInline literal:
                    ReadLiteral(literal);
                    return;
            }

            if (node is ContainerBlock block)
            {
                foreach (var child in block)
                    Visit(child);
            }
            if (node is LeafBlock { Inline: { } inline })
                Visit(inline);
            if (node is ContainerInline container)
            {
                foreach (var child in container)
                    Visit(child);
            }
        }

        private void ReadEmphasis(EmphasisInline emphasis)
        {
            var style = (emphasis.DelimiterChar, emphasis.DelimiterCount) switch
            {
                ('~', _) => ChatTextStyle.Strike,
                (_, 2) => ChatTextStyle.Bold,
                _ => ChatTextStyle.Italic
            };

            int start = emphasis.Span.Start;
            int end = emphasis.Span.End;
            Hide(start, start + emphasis.DelimiterCount - 1);
            Hide(end - emphasis.DelimiterCount + 1, end);
            for (int i = Math.Max(0, start); i <= end && i < _sourceStyles.Length; i++)
                _sourceStyles[i] |= style;
        }

        private bool ReadLink(LinkInline link)
        {
            // Images and non-web destinations remain text and never load remote content.
            if (link.IsImage || !TryWebUri(link.Url, out var uri))
                return false;

            if (link.FirstChild is { } first && link.LastChild is { } last)
            {
                int labelStart = first.Span.Start;
                int labelEnd = last.Span.End;
                Hide(link.Span.Start, labelStart - 1);
                Hide(labelEnd + 1, link.Span.End);
                _sourceLinks.Add(new ChatLink(labelStart, labelEnd - labelStart + 1, uri!));
            }
            return true;
        }

        private void ReadLiteral(LiteralInline literal)
        {
            // Markdig resolves backslash escapes; mention placeholders are restored from the source.
            string content = RestoreMentions(literal.Content.ToString(), literal.Span);
            int start = literal.Span.Start;
            int end = literal.Span.End;
            if (start >= 0 && end < _source.Length && content != _source[start..(end + 1)])
                Replace(literal, content);
        }

        private void Hide(int start, int end)
        {
            for (int i = Math.Max(0, start); i <= end && i < _hidden.Length; i++)
                _hidden[i] = true;
        }

        private void Replace(MarkdownObject node, string text, ChatTextStyle style = ChatTextStyle.None) =>
            _replacements[node.Span.Start] = new SourceReplacement(node.Span.End, text, style);

        private string RestoreMentions(string text, SourceSpan sourceSpan)
        {
            if (!text.Contains(MentionPlaceholder))
                return text;

            var result = new StringBuilder(text);
            int position = 0;
            foreach (var mention in _body.Mentions)
            {
                if (mention.Start < sourceSpan.Start || mention.End > sourceSpan.End + 1)
                    continue;

                // Names are opaque to Markdown, even when they contain punctuation or backticks.
                int found = text.IndexOf(MentionPlaceholder, position);
                if (found < 0)
                    break;

                string name = _source.Substring(mention.Start, mention.Length);
                result.Remove(found, mention.Length).Insert(found, name);
                position = found + mention.Length;
            }
            return result.ToString();
        }

        private TextProjection ProjectText()
        {
            var output = new StringBuilder(_source.Length);
            var outputStyles = new List<ChatTextStyle>();
            var sourceToDisplay = new int[_source.Length + 1];
            int position = 0;

            while (position < _source.Length)
            {
                sourceToDisplay[position] = output.Length;
                if (_replacements.TryGetValue(position, out var replacement))
                {
                    int outputStart = output.Length;
                    output.Append(replacement.Text);
                    var style = _sourceStyles[position] | replacement.Style;
                    outputStyles.AddRange(Enumerable.Repeat(style, replacement.Text.Length));

                    // Replaced source characters share one display boundary; the next source
                    // character maps to the end of the replacement on the next iteration.
                    for (int i = position + 1; i <= replacement.End && i < _source.Length; i++)
                        sourceToDisplay[i] = outputStart;
                    position = replacement.End + 1;
                }
                else
                {
                    if (!_hidden[position])
                    {
                        output.Append(_source[position]);
                        outputStyles.Add(_sourceStyles[position]);
                    }
                    position++;
                }
            }

            sourceToDisplay[_source.Length] = output.Length;
            return new TextProjection(output.ToString(), CollectStyleSpans(outputStyles), sourceToDisplay);
        }

        private static IReadOnlyList<ChatStyleSpan> CollectStyleSpans(IReadOnlyList<ChatTextStyle> styles)
        {
            var spans = new List<ChatStyleSpan>();
            int start = 0;
            while (start < styles.Count)
            {
                int end = start + 1;
                while (end < styles.Count && styles[end] == styles[start])
                    end++;
                if (styles[start] != ChatTextStyle.None)
                    spans.Add(new ChatStyleSpan(start, end - start, styles[start]));
                start = end;
            }
            return spans;
        }

        private IReadOnlyList<ChatMentionSpan> ProjectMentions(TextProjection projection)
        {
            var mentions = new List<ChatMentionSpan>();
            foreach (var mention in _body.Mentions)
            {
                int start = projection.SourceToDisplay[mention.Start];
                int end = projection.SourceToDisplay[mention.End];
                if (end > start && !projection.IntersectsCode(start, end))
                    mentions.Add(mention with { Start = start, Length = end - start });
            }
            return mentions;
        }

        private IReadOnlyList<ChatLink> ProjectLinks(TextProjection projection, IReadOnlyList<ChatMentionSpan> mentions)
        {
            bool IntersectsMention(ChatLink link) =>
                mentions.Any(mention => mention.Start < link.End && link.Start < mention.End);

            var links = new List<ChatLink>();
            foreach (var link in _sourceLinks)
            {
                int start = projection.SourceToDisplay[link.Start];
                int end = projection.SourceToDisplay[link.End];
                var displayedLink = link with { Start = start, Length = end - start };
                if (displayedLink.Length > 0 && !IntersectsMention(displayedLink))
                    links.Add(displayedLink);
            }

            foreach (var link in ChatLinks.Find(projection.Text))
            {
                bool overlapsLink = links.Any(existing => existing.Start < link.End && link.Start < existing.End);
                if (!projection.IntersectsCode(link.Start, link.End) && !overlapsLink && !IntersectsMention(link))
                    links.Add(link);
            }
            return links.OrderBy(link => link.Start).ToArray();
        }
    }

    private sealed record SourceReplacement(int End, string Text, ChatTextStyle Style);

    private sealed record TextProjection(string Text, IReadOnlyList<ChatStyleSpan> Styles, int[] SourceToDisplay)
    {
        public bool IntersectsCode(int start, int end) => Styles.Any(span =>
            span.Style.HasFlag(ChatTextStyle.Code) && span.Start < end && start < span.End);
    }
}
