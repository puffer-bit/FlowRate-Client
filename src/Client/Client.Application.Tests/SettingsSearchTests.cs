using Client.Application.Settings.Search;
using Xunit;

namespace Client.Application.Tests;

public class SettingsSearchTests
{
    private static SettingSearchEntry Entry(string key, string title, string? description = null, string section = "Section",
        SettingSearchText? invariant = null) => new(key, new SettingSearchText(title, description, section), invariant);

    private static string[] Find(string? query, params SettingSearchEntry[] entries) =>
        SettingsSearch.Find(query, entries, entry => entry).Select(entry => entry.Key).ToArray();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankQueryFindsNothing(string? query)
    {
        Assert.Empty(Find(query, Entry("a", "Input volume")));
    }

    [Fact]
    public void CaseIsIgnored()
    {
        Assert.Equal(["a"], Find("VOLUME", Entry("a", "Input volume")));
    }

    [Fact]
    public void EveryWordHasToOccur()
    {
        var entries = new[] { Entry("a", "Input volume"), Entry("b", "Input device") };

        Assert.Equal(["a"], Find("input vol", entries));
        Assert.Empty(Find("input speaker", entries));
    }

    [Fact]
    public void WordsMayOccurInDifferentTexts()
    {
        Assert.Equal(["a"], Find("microphone echo", Entry("a", "Hear myself", "Use headphones to avoid echo", "Microphone test")));
    }

    [Fact]
    public void TitleOutweighsSectionWhichOutweighsDescription()
    {
        var entries = new[]
        {
            Entry("description", "Hear myself", "Plays the microphone back"),
            Entry("section", "Input level", section: "Microphone test"),
            Entry("title", "Test microphone")
        };

        Assert.Equal(["title", "section", "description"], Find("microphone", entries));
    }

    [Fact]
    public void StartOfWordOutweighsMiddleOfWord()
    {
        var entries = new[] { Entry("middle", "Reconnect"), Entry("start", "Auto connect") };

        Assert.Equal(["start", "middle"], Find("connect", entries));
    }

    [Fact]
    public void EqualMatchesKeepTheirOrder()
    {
        var entries = new[] { Entry("first", "Input device"), Entry("second", "Input volume"), Entry("third", "Input level") };

        Assert.Equal(["first", "second", "third"], Find("input", entries));
    }

    [Fact]
    public void InvariantTextIsMatchedToo()
    {
        var entry = Entry("a", "Громкость входа", section: "Микрофон",
            invariant: new SettingSearchText("Input volume", null, "Microphone"));

        Assert.Equal(["a"], Find("volume", entry));
        Assert.Equal(["a"], Find("громкость", entry));
    }
}
