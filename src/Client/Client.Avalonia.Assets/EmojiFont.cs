using Avalonia;
using Avalonia.Media;

namespace Client.Avalonia.Icons;

public static class EmojiFont
{
    /// <summary>
    /// The platform-compatible color emoji font used throughout the application.
    /// </summary>
    // The Windows font backend can resolve COLRv1 glyphs without rendering their color layers,
    // leaving blank space instead of falling back. Use the native color font on Windows.
    public static readonly FontFamily Family = OperatingSystem.IsWindows()
        ? new FontFamily("Segoe UI Emoji")
        : new FontFamily("avares://Client.Avalonia.Assets/Fonts#Noto Color Emoji");

    /// <summary>
    /// Draws emoji with the selected color font. Without it a character missing from
    /// the text font goes to the system, and Linux often finds it first in a black and white font
    /// (Symbola, DejaVu), or in an emoji font many years old.
    /// </summary>
    public static AppBuilder WithEmojiFont(this AppBuilder builder) =>
        builder.With(new FontManagerOptions
        {
            FontFallbacks = [new FontFallback { FontFamily = Family }]
        });
}
