using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Client.Avalonia.Styles.Animation;
using Client.Avalonia.Styles.Scaling;
using Client.Core.Settings.Themes;

namespace Client.Services;

/// <summary>
/// Implementation of theme service that manages application styling and colors
/// </summary>
public class ThemeService : IThemeService
{
    public void ApplyTheme(ThemeType themeType)
    {
        SetThemeVariant(themeType == ThemeType.Light ? ThemeVariant.Light : ThemeVariant.Dark);
    }

    private static void SetThemeVariant(ThemeVariant variant)
    {
        // Theme dictionaries in DarkTheme/LightTheme.axaml are resolved by the application variant
        Dispatcher.UIThread.Post(() =>
        {
            if (global::Avalonia.Application.Current is not { } app || app.RequestedThemeVariant == variant)
                return;

            WindowCrossFade.Run(() => app.RequestedThemeVariant = variant);
        });
    }

    // Shares of white and black that turn a chosen color into its hovered, pressed and disabled shades;
    // close to the shades the themes define for their own colors
    private const double HoveredShade = 0.11;
    private const double PressedShade = 0.15;
    private const double DisabledShade = 0.38;

    // The last of the merged application dictionaries, so its colors are found before the ones of the themes
    private ResourceDictionary? _customColors;

    public void ApplyCustomColors(CustomColors colors, bool isAnimated = false)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyCustomColors(colors, isAnimated));
            return;
        }

        if (isAnimated)
            WindowCrossFade.Run(() => SwapCustomColors(colors));
        else
            SwapCustomColors(colors);
    }

    private void SwapCustomColors(CustomColors colors)
    {
        if (global::Avalonia.Application.Current is not { } app)
            return;

        var customColors = new ResourceDictionary();
        customColors.ThemeDictionaries[ThemeVariant.Dark] = BuildCustomColors(colors, ThemeVariant.Dark);
        customColors.ThemeDictionaries[ThemeVariant.Light] = BuildCustomColors(colors, ThemeVariant.Light);

        // Filled before it is attached and swapped in one step: every change of the application
        // resources makes all windows look their brushes up again
        var merged = app.Resources.MergedDictionaries;
        var index = _customColors == null ? -1 : merged.IndexOf(_customColors);
        if (index >= 0)
            merged[index] = customColors;
        else
            merged.Add(customColors);

        _customColors = customColors;
    }

    public ThemeColors ResolveColors(CustomColors colors, ThemeType themeType)
    {
        var variant = themeType == ThemeType.Light ? ThemeVariant.Light : ThemeVariant.Dark;

        return new ThemeColors(
            ParseColor(colors.Primary) ?? FindThemeColor("ColorPrimary", variant),
            ParseColor(colors.Secondary) ?? FindThemeColor("ColorSecondary", variant),
            ParseColor(colors.Danger) ?? FindThemeColor("ColorDangerBase", variant),
            ParseColor(colors.Save) ?? FindThemeColor("ColorSaveBase", variant),
            ParseColor(colors.Success) ?? FindThemeColor("ColorSuccess", variant),
            ParseColor(colors.Warning) ?? FindThemeColor("ColorWarning", variant),
            ParseColor(colors.Error) ?? FindThemeColor("ColorError", variant));
    }

    private ResourceDictionary BuildCustomColors(CustomColors colors, ThemeVariant variant)
    {
        var isDark = variant == ThemeVariant.Dark;
        var resources = new ResourceDictionary();

        if (ParseColor(colors.Primary) is { } primary)
        {
            resources["ColorPrimary"] = primary;
            resources["ColorPrimaryLight"] = Mix(primary, Colors.White, HoveredShade);
            resources["ColorPrimaryDark"] = Mix(primary, Colors.Black, PressedShade);
            // The faint shade of disabled buttons and table headers: most of it is the surface it lies on
            var surface = FindThemeColor("ColorDark2", variant);
            resources["ColorPrimaryVeryDark"] = Mix(primary, surface, isDark ? 0.5 : 0.85);

            // The glows of the session backdrop that the themes draw in shades of their primary color;
            // the middle one is a color of its own and stays
            var farGlow = isDark ? Mix(primary, surface, 0.5) : Mix(primary, Colors.White, 0.62);
            var nearGlow = isDark ? Mix(primary, Colors.Black, PressedShade) : Mix(primary, Colors.White, HoveredShade);
            AddGlow(resources, "ColorSessionGlowFar", farGlow, isDark ? (byte)0x80 : (byte)0x99);
            AddGlow(resources, "ColorSessionGlowNear", nearGlow, isDark ? (byte)0x52 : (byte)0x4D);

            // The accent of the Fluent controls the application does not restyle
            resources["SystemAccentColor"] = primary;
            resources["SystemAccentColorLight1"] = Mix(primary, Colors.White, HoveredShade);
            resources["SystemAccentColorLight2"] = Mix(primary, Colors.White, 0.2);
            resources["SystemAccentColorLight3"] = Mix(primary, Colors.White, 0.35);
            resources["SystemAccentColorDark1"] = Mix(primary, Colors.Black, PressedShade);
            resources["SystemAccentColorDark2"] = Mix(primary, Colors.Black, 0.3);
            resources["SystemAccentColorDark3"] = Mix(primary, Colors.Black, 0.45);
        }

        if (ParseColor(colors.Secondary) is { } secondary)
        {
            resources["ColorSecondary"] = secondary;
            // Pressed stands out against the window: lighter in the dark theme, darker in the light one
            resources["ColorSecondaryPressed"] = Mix(secondary, isDark ? Colors.White : Colors.Black, 0.3);
        }

        if (ParseColor(colors.Danger) is { } danger)
            AddButtonShades(resources, "ColorDanger", danger, isDark);

        if (ParseColor(colors.Save) is { } save)
            AddButtonShades(resources, "ColorSave", save, isDark);

        if (ParseColor(colors.Success) is { } success)
            resources["ColorSuccess"] = success;

        if (ParseColor(colors.Warning) is { } warning)
            resources["ColorWarning"] = warning;

        if (ParseColor(colors.Error) is { } error)
            resources["ColorError"] = error;

        return resources;
    }

    private static void AddButtonShades(ResourceDictionary resources, string keyPrefix, Color color, bool isDark)
    {
        resources[keyPrefix + "Base"] = color;
        resources[keyPrefix + "Hovered"] = Mix(color, Colors.White, HoveredShade);
        resources[keyPrefix + "Pressed"] = Mix(color, Colors.Black, PressedShade);
        // Disabled buttons of the light theme are gray whatever their color is
        if (isDark)
            resources[keyPrefix + "Disabled"] = Mix(color, Colors.Black, DisabledShade);
    }

    // A glow fades into its own color with no opacity left, as the ones of the themes do
    private static void AddGlow(ResourceDictionary resources, string key, Color color, byte opacity)
    {
        resources[key] = Color.FromArgb(opacity, color.R, color.G, color.B);
        resources[key + "Edge"] = Color.FromArgb(0, color.R, color.G, color.B);
    }

    /// <summary>
    /// The color the theme itself defines for the key, whatever the user has chosen instead
    /// </summary>
    private Color FindThemeColor(string key, ThemeVariant variant)
    {
        if (global::Avalonia.Application.Current is { } app)
        {
            foreach (var dictionary in app.Resources.MergedDictionaries)
            {
                if (!ReferenceEquals(dictionary, _customColors)
                    && dictionary.TryGetResource(key, variant, out var value) && value is Color color)
                    return color;
            }
        }

        return Colors.Transparent;
    }

    // A stored value that is not a color is ignored; chosen colors are opaque
    private static Color? ParseColor(string? value)
    {
        return value != null && Color.TryParse(value, out var color)
            ? Color.FromRgb(color.R, color.G, color.B)
            : null;
    }

    private static Color Mix(Color color, Color other, double share)
    {
        return Color.FromRgb(MixChannel(color.R, other.R, share), MixChannel(color.G, other.G, share),
            MixChannel(color.B, other.B, share));
    }

    private static byte MixChannel(byte channel, byte other, double share)
    {
        return (byte)Math.Round(channel + (other - channel) * share);
    }

    // What the durations of Motion are multiplied by at each speed
    private const double SlowDurationScale = 1.5;
    private const double FastDurationScale = 0.6;

    public void ApplyAnimations(AnimationSettings settings)
    {
        // Durations are bound to transitions and animations of the views
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyAnimations(settings));
            return;
        }

        var durationScale = settings.Speed switch
        {
            AnimationSpeed.Slow => SlowDurationScale,
            AnimationSpeed.Fast => FastDurationScale,
            _ => 1
        };

        Motion.Apply(settings.IsEnabled, durationScale);
    }

    public void ApplyInterfaceScale(int percent)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyInterfaceScale(percent));
            return;
        }

        var factor = InterfaceScale.Normalize(percent) / 100.0;
        if (factor != WindowScale.Factor)
            WindowCrossFade.Run(() => WindowScale.Apply(factor));
    }
}
