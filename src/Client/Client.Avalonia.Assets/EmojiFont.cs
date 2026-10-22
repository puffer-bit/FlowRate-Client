using Avalonia;
using Avalonia.Media;

namespace Client.Avalonia.Icons;

public static class EmojiFont
{
    /// <summary>
    /// The color emoji bundled with the application (Noto Color Emoji, COLRv1).
    /// </summary>
    public static readonly FontFamily Family = new("avares://Client.Avalonia.Assets/Fonts#Noto Color Emoji");

    /// <summary>
    /// Draws emoji with the bundled font whatever the system has. Without it a character missing from
    /// the text font goes to the system, and Linux often finds it first in a black and white font
    /// (Symbola, DejaVu), or in an emoji font many years old.
    /// </summary>
    public static AppBuilder WithEmojiFont(this AppBuilder builder) =>
        builder.With(new FontManagerOptions
        {
            FontFallbacks = [new FontFallback { FontFamily = Family }]
        });
}
