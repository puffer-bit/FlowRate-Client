using Avalonia.Media;
using Client.Core.Settings.Themes;

namespace Client.Services;

/// <summary>
/// Service for managing application theming and styling
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Apply built-in theme preset to the whole application
    /// </summary>
    /// <param name="themeType">Dark or Light (Custom is not supported yet)</param>
    void ApplyTheme(ThemeType themeType);
    
    /// <summary>
    /// Replace colors of both themes with the ones the user has chosen, at once and in every open window
    /// </summary>
    /// <param name="colors">Chosen colors; a color that is not set goes back to the one of the theme</param>
    /// <param name="isAnimated">
    /// Whether the windows blend into the new colors. Not for a color that is being dragged in a picker:
    /// it changes step by step anyway, and the blend would trail behind the pointer
    /// </param>
    void ApplyCustomColors(CustomColors colors, bool isAnimated = false);

    /// <summary>
    /// The colors the application shows for these choices: the chosen ones, and the ones of the theme for the rest
    /// </summary>
    ThemeColors ResolveColors(CustomColors colors, ThemeType themeType);

    /// <summary>
    /// Switch the animations of the interface on or off and set their speed, at once and in every open window
    /// </summary>
    void ApplyAnimations(AnimationSettings settings);

    /// <summary>
    /// Draw the interface at another size, at once and in every open window
    /// </summary>
    /// <param name="percent">Size in percent of the usual one</param>
    void ApplyInterfaceScale(int percent);
}

/// <summary>
/// Types of notifications with different styling
/// </summary>
public enum NotificationType
{
    Warning,
    Error,
    Info,
    Success,
    Loading,
    Question
}

/// <summary>
/// The colors a user can choose, as they are shown
/// </summary>
public readonly record struct ThemeColors(Color Primary, Color Secondary, Color Danger, Color Save, Color Success,
    Color Warning, Color Error);
