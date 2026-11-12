using Avalonia.Media;

namespace Client.Services;

/// <summary>
/// The colors a user can choose, as they are shown
/// </summary>
public readonly record struct ThemeColors(Color Primary, Color Secondary, Color Danger, Color Save, Color Success,
    Color Warning, Color Error);
