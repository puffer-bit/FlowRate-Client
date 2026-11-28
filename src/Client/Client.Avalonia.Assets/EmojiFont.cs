using Avalonia.Media;

namespace Client.Avalonia.Icons;

public static class EmojiFont
{
    /// <summary>
    /// The platform-compatible color emoji font used throughout the application.
    /// </summary>
    // The Windows font backend can resolve COLRv1 glyphs without rendering their color layers,
    // leaving blank space instead of falling back. Bundle Noto's CBDT/CBLC build for Windows.
    public static readonly FontFamily Family = OperatingSystem.IsWindows()
        ? new FontFamily("avares://Client.Avalonia.Assets/Fonts/EmojiWindows#Noto Color Emoji")
        : new FontFamily("avares://Client.Avalonia.Assets/Fonts/Emoji#Noto Color Emoji");
}
