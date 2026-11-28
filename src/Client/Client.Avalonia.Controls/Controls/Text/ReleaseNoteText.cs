using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace Client.Avalonia.Controls.Text;

/// <summary>Renders the inline Markdown used in published release descriptions.</summary>
public sealed class ReleaseNoteText : SelectableTextBlock
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<ReleaseNoteText, string?>(nameof(Markdown));

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty) Render();
    }

    private void Render()
    {
        Inlines ??= new InlineCollection();
        Inlines.Clear();
        var text = Markdown ?? "";
        var position = 0;
        foreach (Match match in Regex.Matches(text, @"(`[^`]+`|\*\*.+?\*\*|__.+?__|\[[^\]]+\]\(https?://[^)]+\)|https?://[^\s]+)"))
        {
            if (match.Index > position) Inlines.Add(new Run(text[position..match.Index]));
            var token = match.Value;
            if (token.StartsWith('`'))
                Inlines.Add(new Run(token[1..^1]) { FontFamily = new FontFamily("monospace"), FontWeight = FontWeight.Medium });
            else if (token.StartsWith("**", StringComparison.Ordinal) || token.StartsWith("__", StringComparison.Ordinal))
                Inlines.Add(new Run(token[2..^2]) { FontWeight = FontWeight.SemiBold });
            else
            {
                var link = Regex.Match(token, @"^\[([^\]]+)\]\((.+)\)$");
                var url = link.Success ? link.Groups[2].Value : token;
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
                {
                    var label = link.Success ? link.Groups[1].Value : CompactUrl(uri);
                    var button = new HyperlinkButton
                    {
                        Content = label, NavigateUri = uri, Padding = new Thickness(0),
                        MinHeight = 0, Height = 18, BorderThickness = new Thickness(0), FontSize = FontSize, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                    };
                    button.Bind(ForegroundProperty, new DynamicResourceExtension("LinkTextBrush"));
                    ToolTip.SetTip(button, url);
                    Inlines.Add(new InlineUIContainer(button));
                }
                else Inlines.Add(new Run(token));
            }
            position = match.Index + match.Length;
        }
        if (position < text.Length) Inlines.Add(new Run(text[position..]));
    }

    private static string CompactUrl(Uri uri)
    {
        var path = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (uri.Host == "github.com" && path.Length >= 4 && path[2] == "pull") return "#" + path[3];
        if (uri.Host == "github.com" && path.Length >= 4 && path[2] == "compare") return path[0] + "/" + path[1] + " ↗";
        return uri.Host + (uri.AbsolutePath == "/" ? "" : "/…");
    }
}
