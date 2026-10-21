using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Client.Avalonia.Localization.Tests;

/// <summary>
/// Checks the language files themselves. A translation may lack texts, which then are shown in English.
/// </summary>
public partial class StringTableTests
{
    private static readonly StringTable English = Strings.Invariant;

    public static TheoryData<string> Translations =>
        new(Strings.Languages.Where(language => language != English).Select(language => language.Code));

    public static TheoryData<string> AllLanguages => new(Strings.Languages.Select(language => language.Code));

    [Fact]
    public void EnglishAndRussianAreBuiltIn()
    {
        Assert.Equal("en", Strings.Languages[0].Code);
        Assert.Contains(Strings.Languages, language => language.Code == "ru");
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void TranslationHasNoKeysEnglishLacks(string code)
    {
        Assert.Empty(Table(code).Keys.Except(English.Keys).Order());
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void TranslationKeepsThePlaceholders(string code)
    {
        var table = Table(code);
        var different = table.Keys
            .Where(key => English.Find(key) is { } text && !Placeholders(text).SetEquals(Placeholders(table.Find(key)!)))
            .Order();

        Assert.Empty(different);
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryTextIsAValidMessage(string code)
    {
        var table = Table(code);
        var broken = new List<string>();

        foreach (var key in table.Keys)
        {
            var text = table.Find(key)!;
            if (text.Length == 0)
                broken.Add($"{key}: blank");

            // Numbers that fall into every plural form of every language
            foreach (var number in (int[])[0, 1, 2, 3, 5, 11, 21, 100, 1000000])
            {
                try
                {
                    table.Format(text, Placeholders(text).ToDictionary(name => name, _ => (object?)number));
                }
                catch (Exception exception)
                {
                    broken.Add($"{key}: {exception.Message}");
                    break;
                }
            }
        }

        Assert.Empty(broken);
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void LanguageNamesItself(string code)
    {
        Assert.NotEqual(code, Table(code).Name);
    }

    [Theory]
    [InlineData(1, "1 файл")]
    [InlineData(2, "2 файла")]
    [InlineData(5, "5 файлов")]
    [InlineData(11, "11 файлов")]
    [InlineData(21, "21 файл")]
    [InlineData(112, "112 файлов")]
    public void PluralFormFollowsTheRulesOfTheLanguage(int count, string expected)
    {
        const string text = "{count, plural, one{# файл} few{# файла} many{# файлов} other{# файла}}";

        Assert.Equal(expected, Table("ru").Format(text, new Dictionary<string, object?> { ["count"] = count }));
    }

    [Fact]
    public void AiTranslationIsMarkedInTheFile()
    {
        Assert.True(Table("es").IsMachineTranslated);
        Assert.True(Table("zh").IsMachineTranslated);
        Assert.True(Table("ja").IsMachineTranslated);
        Assert.False(Table("en").IsMachineTranslated);
        Assert.True(Load("""{ "@@locale": "xx", "@@x-translatedBy": "ai" }""").IsMachineTranslated);
        Assert.False(Load("""{ "@@locale": "xx", "@@x-translatedBy": "human" }""").IsMachineTranslated);
    }

    [Fact]
    public void DescriptionsAreNotTexts()
    {
        var table = Load("""{ "@@locale": "xx", "a": "Text {name}", "@a": { "placeholders": { "name": {} } } }""");

        Assert.Equal("xx", table.Code);
        Assert.Equal(["a"], table.Keys);
    }

    [Theory]
    [InlineData("""{ "a": "Text" }""", "@@locale")]
    [InlineData("""{ "@@locale": "xx", "a": "One", "a": "Two" }""", "twice")]
    [InlineData("""{ "@@locale": "xx", "a": 5 }""", "string")]
    [InlineData("""[ "a" ]""", "object")]
    [InlineData("""{ "@@locale": "xx", """, "JSON")]
    public void BrokenFileIsRejectedWithAReason(string json, string reason)
    {
        var exception = Assert.Throws<InvalidDataException>(() => Load(json));

        Assert.Contains(reason, exception.Message);
    }

    private static StringTable Table(string code) => Strings.Languages.First(language => language.Code == code);

    private static StringTable Load(string json) =>
        StringTable.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)), "test.arb");

    // The names of {name} and {name, plural, ...}; the text of a plural form opens with a brace as well,
    // but no comma or closing brace follows its first word
    internal static HashSet<string> Placeholders(string text) =>
        PlaceholderPattern().Matches(text).Select(match => match.Groups[1].Value).ToHashSet();

    [GeneratedRegex(@"\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*[,}]")]
    private static partial Regex PlaceholderPattern();
}
