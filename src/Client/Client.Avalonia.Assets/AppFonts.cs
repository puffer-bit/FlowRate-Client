using Avalonia;
using Avalonia.Media;

namespace Client.Avalonia.Icons;

/// <summary>Bundled text and emoji fonts, independent of the fonts installed on the device.</summary>
public static class AppFonts
{
    private const string Latin = "avares://Client.Avalonia.Assets/Fonts/NotoSans#Noto Sans";
    private const string Japanese = "avares://Client.Avalonia.Assets/Fonts/NotoSansJP#Noto Sans JP";
    private const string Chinese = "avares://Client.Avalonia.Assets/Fonts/NotoSansSC#Noto Sans SC";
    private const string DefaultFamily = Latin + ", " + Chinese + ", " + Japanese;

    private static readonly FontFamily Default = new(DefaultFamily);
    private static readonly FontFamily JapaneseFirst = new(Latin + ", " + Japanese + ", " + Chinese);

    // Shared Han characters have regional forms. Change their preferred family along with the UI language.
    public static FontFamily ForLanguage(string? language) =>
        language?.StartsWith("ja", StringComparison.OrdinalIgnoreCase) == true ? JapaneseFirst : Default;

    public static AppBuilder WithAppFonts(this AppBuilder builder) =>
        builder.With(new FontManagerOptions
        {
            DefaultFamilyName = DefaultFamily,
            FontFallbacks = [new FontFallback { FontFamily = EmojiFont.Family }]
        });
}
