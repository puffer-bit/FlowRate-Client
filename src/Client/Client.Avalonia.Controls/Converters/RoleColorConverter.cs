using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Client.Avalonia.Controls.Converters;

/// <summary>
/// The color a role was given, 0xRRGGBB, as a brush. A role without one gets no value, so the style of the
/// control decides.
/// </summary>
public sealed class RoleColorConverter : IValueConverter
{
    public static RoleColorConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int rgb
            ? new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb))
            : AvaloniaProperty.UnsetValue;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
