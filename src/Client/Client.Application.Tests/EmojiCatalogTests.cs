using System.Globalization;
using Client.Core.Chat;
using Xunit;

namespace Client.Application.Tests;

public class EmojiCatalogTests
{
    [Fact]
    public void CatalogHasUniqueWholeSequencesInAllCategories()
    {
        var entries = EmojiCatalog.Entries;
        Assert.True(entries.Count > 1800);
        Assert.Equal(entries.Count, entries.Select(entry => entry.Text).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 9), entries.Select(entry => entry.Category).Distinct().Order());
        Assert.All(entries, entry =>
        {
            Assert.Single(StringInfo.ParseCombiningCharacters(entry.Text));
            Assert.False(string.IsNullOrWhiteSpace(entry.EnglishName));
            Assert.False(string.IsNullOrWhiteSpace(entry.RussianName));
        });
        Assert.Contains(entries, entry => entry.Text == "❤️‍🔥");
        Assert.Contains(entries, entry => entry.Text == "🇷🇺");
        Assert.Contains(entries, entry => entry.Text == "1️⃣");
    }

    [Theory]
    [InlineData("КОШКА", "🐈")]
    [InlineData("cat", "🐈")]
    [InlineData("красное сердце", "❤️")]
    [InlineData("red heart", "❤️")]
    [InlineData("❤️", "❤️")]
    public void SearchMatchesBothLanguagesAndIgnoresBrowsingCategory(string query, string expected)
    {
        Assert.Contains(EmojiCatalog.Search(query, 8), entry => entry.Text == expected);
    }

    [Fact]
    public void BrowseFiltersCategoryAndUnknownSearchIsEmpty()
    {
        Assert.All(EmojiCatalog.Search("  ", 3), entry => Assert.Equal(3, entry.Category));
        Assert.Empty(EmojiCatalog.Search("no-such-emoji-123456"));
    }

    [Fact]
    public void RecentListRestoresKnownEntriesAndMovesDuplicatesToFront()
    {
        var recent = new RecentEmoji(["unknown", "😀", "❤️‍🔥", "😀"]);
        Assert.Equal(["😀", "❤️‍🔥"], recent.Entries.Select(entry => entry.Text));
        recent.Record(EmojiCatalog.Entries.Single(entry => entry.Text == "❤️‍🔥"));
        Assert.Equal(["❤️‍🔥", "😀"], recent.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void RecentListIsBoundedAndCanRoundTrip()
    {
        var recent = new RecentEmoji();
        foreach (var entry in EmojiCatalog.Entries.Take(RecentEmoji.Capacity + 10)) recent.Record(entry);
        Assert.Equal(RecentEmoji.Capacity, recent.Entries.Count);
        var restored = new RecentEmoji(recent.Entries.Select(entry => entry.Text));
        Assert.Equal(recent.Entries, restored.Entries);
    }
}
