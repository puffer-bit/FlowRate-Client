using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Client.Avalonia.Controls.Text;
using Client.Core.Chat;
using Client.ViewModels.Sessions.Chat;
using Xunit;

namespace Client.Application.Tests;

public class ChatMarkdownTests
{
    private static ChatMarkdownDocument Parse(string text) => ChatMarkdown.Parse(new(text, []));

    [Theory]
    [InlineData("**bold**", "bold", ChatTextStyle.Bold)]
    [InlineData("*italic*", "italic", ChatTextStyle.Italic)]
    [InlineData("~~strike~~", "strike", ChatTextStyle.Strike)]
    [InlineData("***both***", "both", ChatTextStyle.Bold | ChatTextStyle.Italic)]
    [InlineData("`**literal**`", "**literal**", ChatTextStyle.Code)]
    [InlineData("`` `code` ``", "`code`", ChatTextStyle.Code)]
    public void StylesHaveDisplayedCoordinates(string source, string expected, ChatTextStyle style)
    {
        var result = Parse(source);
        Assert.Equal(expected, result.Text);
        var span = Assert.Single(result.Styles);
        Assert.Equal(new ChatStyleSpan(0, expected.Length, style), span);
    }

    [Theory]
    [InlineData("first\nsecond\n\nthird")]
    [InlineData("# heading\n- item\n> quote")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("unfinished **bold")]
    [InlineData("  spaces  \n\n")]
    public void PlainAndUnsupportedSyntaxStayLiteral(string text) => Assert.Equal(text, Parse(text).Text);

    [Fact]
    public void LinksMapLabelsAndDestinationsSeparately()
    {
        var result = Parse("**see [*site*](https://example.com/path)** and www.example.org");
        Assert.Equal("see site and www.example.org", result.Text);
        Assert.Equal(2, result.Links.Count);
        Assert.Equal(new ChatLink(4, 4, new Uri("https://example.com/path")), result.Links[0]);
        Assert.Equal("www.example.org", result.Text.Substring(result.Links[1].Start, result.Links[1].Length));
        Assert.Contains(result.Styles, span => span.Start == 4 && span.Style == (ChatTextStyle.Bold | ChatTextStyle.Italic));
    }

    [Fact]
    public void BareAddressKeepsMarkdownPunctuationInsideItsPath()
    {
        const string address = "https://example.com/a*b*c";
        var result = Parse(address);
        Assert.Equal(address, result.Text);
        Assert.Equal(address, Assert.Single(result.Links).Uri.AbsoluteUri);
        Assert.Empty(result.Styles);
    }

    [Theory]
    [InlineData("[unsafe](javascript:alert(1))")]
    [InlineData("[file](file:///etc/passwd)")]
    [InlineData("![image](https://example.com/image.png)")]
    public void UnsafeLinksAndImagesStayLiteral(string text)
    {
        var result = Parse(text);
        Assert.Equal(text, result.Text);
        // A plain web address can still be copied/opened, but no remote image is rendered.
        Assert.DoesNotContain(result.Links, link => link.Uri.Scheme is not "https" and not "http");
    }

    [Fact]
    public void CodeDoesNotProduceLinksOrActiveMentions()
    {
        var body = new ChatMessageBody("`@name https://example.com`", [new(1, 5, true, 42)]);
        var result = ChatMarkdown.Parse(body);
        Assert.Equal("@name https://example.com", result.Text);
        Assert.Empty(result.Mentions);
        Assert.Empty(result.Links);
    }

    [Fact]
    public void FencedCodePreservesContentWithoutLanguageOrDelimiters()
    {
        var result = Parse("before\n```cs\n**literal**\nhttps://example.com\n```\nafter");
        Assert.Equal("before\n**literal**\nhttps://example.com\nafter", result.Text);
        Assert.Empty(result.Links);
        Assert.Contains(result.Styles, span => span.Style == ChatTextStyle.Code);
    }

    [Fact]
    public void MentionNamesAreOpaqueAndTheirDisplayedOffsetsFollowMarkdown()
    {
        const string name = "@**[www.example.com]`user`**";
        string source = "**hello " + name + "** ❤️";
        var result = ChatMarkdown.Parse(new(source, [new(8, name.Length, true, 42)]));
        Assert.Equal("hello " + name + " ❤️", result.Text);
        Assert.Equal(new ChatMentionSpan(6, name.Length, true, 42), Assert.Single(result.Mentions));
        Assert.Empty(result.Links);
    }

    [Fact]
    public void EscapesAndEntitiesAreRenderedBeforeMappingFollowingMentions()
    {
        const string source = "\\*literal\\* &amp; **@name**";
        var result = ChatMarkdown.Parse(new(source, [new(source.IndexOf('@'), 5, false, 7)]));
        Assert.Equal("*literal* & @name", result.Text);
        Assert.Equal(new ChatMentionSpan(12, 5, false, 7), Assert.Single(result.Mentions));
    }

    [Fact]
    public Task TextBlockUsesDisplayedTextForCopyingAndResetsRecycledRuns() => TestUiThread.RunAsync(() =>
    {
        var block = new SelectableTextBlock();
        ChatMessageText.SetBody(block, new("**hello** [site](https://example.com)", []));
        Assert.Equal("hello site", ChatMessageText.GetText(block));
        Assert.Contains(block.Inlines!.OfType<Run>(), run => run.Text == "hello" && run.FontWeight == FontWeight.Bold);
        ChatMessageText.SetBody(block, new("plain", []));
        Assert.Equal("plain", ChatMessageText.GetText(block));
        Assert.DoesNotContain(block.Inlines!.OfType<Run>(), run => run.Classes.Contains("link"));
    });

    [Fact]
    public Task ExistingCodeAndLinkRunsFollowThemeChanges() => TestUiThread.RunAsync(() =>
    {
        global::Avalonia.AppBuilder.Configure<global::Avalonia.Application>()
            .UseStandardRuntimePlatformSubsystem()
            .UseWindowingSubsystem(() => { }, "Tests")
            .UseRenderingSubsystem(global::Avalonia.Skia.SkiaPlatform.Initialize, "Skia")
            .UseTextShapingSubsystem(() => { }, "Tests")
            .SetupWithoutStarting();
        var block = new TextBlock { Classes = { "msg-body" } };
        var scope = new ThemeVariantScope { Child = block, RequestedThemeVariant = ThemeVariant.Light };
        scope.Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary
        {
            ["SecondaryTextBrush"] = Brushes.DimGray, ["LinkTextBrush"] = Brushes.Blue,
            ["ColorOverlayMedium"] = Color.Parse("#15000000")
        };
        scope.Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary
        {
            ["SecondaryTextBrush"] = Brushes.LightGray, ["LinkTextBrush"] = Brushes.LightBlue,
            ["ColorOverlayMedium"] = Color.Parse("#15FFFFFF")
        };
        scope.Styles.Add(new StyleInclude(new Uri("avares://Client.Avalonia.Styles/"))
        {
            Source = new Uri("avares://Client.Avalonia.Styles/Styles/CustomViews/ChatMessageTextStyle.axaml")
        });
        ChatMessageText.SetBody(block, new("`code` [site](https://example.com)", []));
        block.ApplyStyling();
        var code = block.Inlines!.OfType<Run>().Single(run => run.Classes.Contains("code"));
        var link = block.Inlines!.OfType<Run>().Single(run => run.Classes.Contains("link"));
        code.ApplyStyling();
        link.ApplyStyling();
        Assert.Equal(Brushes.DimGray, code.Foreground);
        Assert.Equal(Brushes.Blue, link.Foreground);
        Assert.Equal(Color.Parse("#15000000"), Assert.IsAssignableFrom<ISolidColorBrush>(code.Background).Color);
        scope.RequestedThemeVariant = ThemeVariant.Dark;
        Assert.Equal(Brushes.LightGray, code.Foreground);
        Assert.Equal(Brushes.LightBlue, link.Foreground);
        Assert.Equal(Color.Parse("#15FFFFFF"), Assert.IsAssignableFrom<ISolidColorBrush>(code.Background).Color);
        scope.RequestedThemeVariant = ThemeVariant.Light;
        Assert.Equal(Brushes.DimGray, code.Foreground);
        Assert.Equal(Brushes.Blue, link.Foreground);
        Assert.Equal(Color.Parse("#15000000"), Assert.IsAssignableFrom<ISolidColorBrush>(code.Background).Color);
    });

    [Fact]
    public void ItalicCanBeAddedToAndRemovedFromBoldSelection()
    {
        var edit = ChatMarkdownEdit.Create("**word**", 2, 6, ChatMarkdownFormat.Italic);
        Assert.Equal("***word***", "**" + edit.Replacement + "**");
        var remove = ChatMarkdownEdit.Create("***word***", 3, 7, ChatMarkdownFormat.Italic);
        Assert.Equal("**word**", remove.Replacement.Insert(0, "**") + "**");
    }

    [Fact]
    public Task ComposerKeepsSelectionAndRejectsWholeEditAtLengthLimit() => TestUiThread.RunAsync(() =>
    {
        var input = new TextBox { Text = "hello world", SelectionStart = 6, SelectionEnd = 11 };
        Assert.True(ChatInputFormatting.Apply(input, ChatMarkdownFormat.Bold));
        Assert.Equal("hello **world**", input.Text);
        Assert.Equal("world", input.SelectedText);
        input.Undo();
        Assert.Equal("hello world", input.Text);
        input.Redo();
        Assert.Equal("hello **world**", input.Text);
        input.SelectionStart = 8;
        input.SelectionEnd = 13;
        Assert.True(ChatInputFormatting.Apply(input, ChatMarkdownFormat.Bold));
        Assert.Equal("hello world", input.Text);
        input.MaxLength = 12;
        Assert.False(ChatInputFormatting.Apply(input, ChatMarkdownFormat.Bold));
        Assert.Equal("hello world", input.Text);
        input.IsReadOnly = true;
        Assert.False(ChatInputFormatting.Apply(input, ChatMarkdownFormat.Italic));
    });

    [Theory]
    [InlineData("hello", ChatMarkdownFormat.Bold, "hello")]
    [InlineData("*literal*", ChatMarkdownFormat.Code, "*literal*")]
    [InlineData("`a` and ``b``", ChatMarkdownFormat.Code, "`a` and ``b``")]
    [InlineData("  padded  ", ChatMarkdownFormat.Code, "  padded  ")]
    [InlineData("line\n```\nline", ChatMarkdownFormat.CodeBlock, "line\n```\nline")]
    public void FormattingCommandsProduceMarkdownThatRoundTrips(string text, ChatMarkdownFormat format, string expected)
    {
        var edit = ChatMarkdownEdit.Create(text, text.Length, 0, format);
        Assert.Equal(expected, Parse(edit.Replacement).Text.TrimEnd('\n'));
        Assert.Equal(text, edit.Replacement.Substring(edit.SelectionStart, edit.SelectionEnd - edit.SelectionStart));
    }
}
