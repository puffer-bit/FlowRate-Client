using Client.Core.Chat;
using Xunit;

namespace Client.Application.Tests;

public class ChatEmojiTests
{
    private static string[] Find(string? text) =>
        ChatEmoji.FindPresentation(text).Select(span => text!.Substring(span.Start, span.Length)).ToArray();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("just a message")]
    // Emoji missing from the text font are found by the font fallback
    [InlineData("hi \U0001F600\U0001F44D\U0001F525")]
    // U+FE0E asks for the text symbol
    [InlineData("I \u2764\uFE0E it")]
    public void TextWithoutPresentationSelectorHasNoSpans(string? text)
    {
        Assert.Empty(ChatEmoji.FindPresentation(text));
    }

    [Fact]
    public void EmojiIsFoundWhereItStands()
    {
        var span = Assert.Single(ChatEmoji.FindPresentation("I \u2764\uFE0F it"));

        Assert.Equal(2, span.Start);
        Assert.Equal(2, span.Length);
    }

    [Fact]
    public void SequenceStaysWhole()
    {
        // A heart on fire, a keycap and a rainbow flag
        Assert.Equal(["\u2764\uFE0F\u200D\U0001F525"], Find("a \u2764\uFE0F\u200D\U0001F525 b"));
        Assert.Equal(["1\uFE0F\u20E3"], Find("1\uFE0F\u20E3 first"));
        Assert.Equal(["\U0001F3F3\uFE0F\u200D\U0001F308"], Find("\U0001F3F3\uFE0F\u200D\U0001F308"));
    }

    [Fact]
    public void EmojiNextToEachOtherMakeOneSpan()
    {
        Assert.Equal(["\u2764\uFE0F\u2600\uFE0F", "\u263A\uFE0F"], Find("\u2764\uFE0F\u2600\uFE0F and \u263A\uFE0F"));
    }

    [Fact]
    public void SelectorAloneIsLeftOut()
    {
        Assert.Empty(ChatEmoji.FindPresentation("\uFE0F text"));
    }

    [Theory]
    [InlineData("\U0001F600")]
    [InlineData("\U0001F600\U0001F44D\U0001F525")]
    [InlineData(" \U0001F600  \U0001F44D ")]
    [InlineData("\u2764\uFE0F")]
    [InlineData("\u263A")]
    [InlineData("1\uFE0F\u20E3")]
    // A family, a waving hand with a skin tone and a flag
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467\U0001F44B\U0001F3FD\U0001F1FA\U0001F1E6")]
    [InlineData("\u2764\uFE0F\u200D\U0001F525")]
    public void FewEmojiAloneAreJumbo(string text)
    {
        Assert.True(ChatEmoji.IsJumbo(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hi \U0001F600")]
    [InlineData("\U0001F600\U0001F600\U0001F600\U0001F600")]
    [InlineData("1")]
    [InlineData("\u2764\uFE0E")]
    [InlineData("\U0001F600!")]
    public void TextOrManyEmojiAreNotJumbo(string? text)
    {
        Assert.False(ChatEmoji.IsJumbo(text));
    }
}
