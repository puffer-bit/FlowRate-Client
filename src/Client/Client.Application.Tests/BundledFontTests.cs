using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Client.Avalonia.Icons;
using Client.Core.Chat;
using Xunit;

namespace Client.Application.Tests;

public sealed class BundledFontTests
{
    [Theory]
    [InlineData(FontWeight.Normal, FontStyle.Normal)]
    [InlineData(FontWeight.Medium, FontStyle.Normal)]
    [InlineData(FontWeight.SemiBold, FontStyle.Normal)]
    [InlineData(FontWeight.Bold, FontStyle.Normal)]
    [InlineData(FontWeight.Normal, FontStyle.Italic)]
    [InlineData(FontWeight.Bold, FontStyle.Italic)]
    public Task TextWeightsUseRealBundledFaces(FontWeight weight, FontStyle style) => TestUiThread.RunAsync(() =>
    {
        var glyphs = new Typeface(AppFonts.ForLanguage("en"), style, weight).GlyphTypeface;
        Assert.Equal("Noto Sans", string.IsNullOrEmpty(glyphs.TypographicFamilyName) ? glyphs.FamilyName : glyphs.TypographicFamilyName);
        Assert.Equal(weight, glyphs.Weight);
        Assert.Equal(style, glyphs.Style);
        Assert.Equal(FontSimulations.None, glyphs.FontSimulations);
        foreach (var rune in "English Español Привет Ёё".EnumerateRunes())
            Assert.True(glyphs.CharacterToGlyphMap.ContainsGlyph(rune.Value), $"Missing {rune}");
    });

    [Theory]
    [InlineData("zh", "中文设置骨", "Noto Sans SC")]
    [InlineData("ja", "日本語の設定骨", "Noto Sans JP")]
    public Task CjkUsesBundledFacesForTheSelectedLanguage(string language, string text, string expected) =>
        TestUiThread.RunAsync(() =>
        {
            foreach (var rune in text.EnumerateRunes())
            {
                Assert.True(FontManager.Current.TryMatchCharacter(rune.Value, FontStyle.Normal, FontWeight.Normal,
                    FontStretch.Normal, AppFonts.ForLanguage(language), CultureInfo.GetCultureInfo(language), out var face));
                Assert.Equal(expected, face.GlyphTypeface.FamilyName);
                Assert.StartsWith("avares://Client.Avalonia.Assets/", face.FontFamily.Key!.Source.AbsoluteUri);
            }
        });

    [Theory]
    [InlineData("Emoji")]
    [InlineData("EmojiWindows")]
    public Task BothEmojiBuildsShapeTheEntirePickerWithoutMissingGlyphs(string folder) => TestUiThread.RunAsync(() =>
    {
        var family = new FontFamily($"avares://Client.Avalonia.Assets/Fonts/{folder}#Noto Color Emoji");
        var glyphs = new Typeface(family).GlyphTypeface;
        Assert.Equal("Noto Color Emoji", glyphs.FamilyName);

        foreach (var emoji in EmojiCatalog.Entries)
        {
            using var shaped = TextShaper.Current.ShapeText(emoji.Text.AsMemory(), new TextShaperOptions(glyphs, 32));
            Assert.True(shaped.Length == 1, $"Emoji sequence {emoji.Text} did not form one glyph in {folder}");
            for (var i = 0; i < shaped.Length; i++)
                Assert.True(shaped[i].GlyphIndex != 0, $"Missing {emoji.Text} in {folder}");
        }
    });
}
