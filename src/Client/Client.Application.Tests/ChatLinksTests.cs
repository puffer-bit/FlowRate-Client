using Client.Core.Chat;
using Xunit;

namespace Client.Application.Tests;

public class ChatLinksTests
{
    private static string[] Find(string? text) =>
        ChatLinks.Find(text).Select(link => text!.Substring(link.Start, link.Length)).ToArray();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("just a message")]
    [InlineData("version 1.2.3 of file.txt")]
    public void TextWithoutAddressesHasNoLinks(string? text)
    {
        Assert.Empty(ChatLinks.Find(text));
    }

    [Fact]
    public void AddressIsFoundWhereItStands()
    {
        var link = Assert.Single(ChatLinks.Find("look at https://example.com/a?b=1#c now"));

        Assert.Equal(8, link.Start);
        Assert.Equal("https://example.com/a?b=1#c".Length, link.Length);
        Assert.Equal("https://example.com/a?b=1#c", link.Uri.AbsoluteUri);
    }

    [Fact]
    public void EveryAddressIsFound()
    {
        Assert.Equal(["http://a.example", "https://b.example/path"],
            Find("http://a.example and\nhttps://b.example/path"));
    }

    [Fact]
    public void AddressWithoutSchemeOpensOverHttps()
    {
        var link = Assert.Single(ChatLinks.Find("www.example.com/page"));

        Assert.Equal("https://www.example.com/page", link.Uri.AbsoluteUri);
        Assert.Equal("www.example.com/page".Length, link.Length);
    }

    [Fact]
    public void SchemeIsFoundInAnyCase()
    {
        var link = Assert.Single(ChatLinks.Find("HTTPS://Example.com/Path"));

        Assert.Equal(Uri.UriSchemeHttps, link.Uri.Scheme);
    }

    [Theory]
    [InlineData("see https://example.com.", "https://example.com")]
    [InlineData("https://example.com/a, then", "https://example.com/a")]
    [InlineData("is it https://example.com/a?!", "https://example.com/a")]
    [InlineData("(see https://example.com/a)", "https://example.com/a")]
    [InlineData("\"https://example.com/a\"", "https://example.com/a")]
    [InlineData("«https://example.com/a».", "https://example.com/a")]
    public void PunctuationAroundAddressIsLeftOut(string text, string expected)
    {
        Assert.Equal([expected], Find(text));
    }

    [Fact]
    public void BracketsOfAddressStay()
    {
        Assert.Equal(["https://en.wikipedia.org/wiki/Flow_(disambiguation)"],
            Find("(https://en.wikipedia.org/wiki/Flow_(disambiguation))"));
    }

    [Fact]
    public void NonLatinAddressIsFound()
    {
        Assert.Equal(["https://пример.рф/страница"], Find("вот https://пример.рф/страница, смотри"));
    }

    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("someone@www.example.com")]
    [InlineData("xhttps://example.com")]
    [InlineData("https://")]
    [InlineData("www.")]
    public void OnlyWebAddressesAreLinks(string text)
    {
        Assert.Empty(ChatLinks.Find(text));
    }
}
