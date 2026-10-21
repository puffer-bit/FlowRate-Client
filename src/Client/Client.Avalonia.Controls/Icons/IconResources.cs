using Avalonia.Media;

namespace Client.Avalonia.Controls.Icons;

/// <summary>
/// Resolves icons declared as DrawingImage resources in
/// avares://Client.Avalonia.Assets/IconImages.axaml by their resource key
/// </summary>
public static class IconResources
{
    /// <summary>
    /// Get icon image by resource key, or null when the key is unknown
    /// </summary>
    public static IImage? Get(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var app = global::Avalonia.Application.Current;
        if (app is null)
            return null;

        return app.TryGetResource(key, app.ActualThemeVariant, out var resource)
            ? resource as IImage
            : null;
    }
}
